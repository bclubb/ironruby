﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Scripting.Utils;

namespace Microsoft.Scripting.Interpreter {

    public class ExceptionHandler {
        public Type ExceptionType;
        public int StartIndex, EndIndex;
        public int JumpToIndex, EndHandlerIndex;
        public bool PushException;

        public bool IsFault { get { return ExceptionType == null; } }

        public bool Matches(Type exceptionType, int index) {
            if (index >= StartIndex && index < EndIndex) {
                if (ExceptionType == null || ExceptionType.IsAssignableFrom(exceptionType)) {
                    return true;
                }
            }
            return false;
        }

        public bool IsBetterThan(ExceptionHandler other) {
            if (other == null) return true;

            if (StartIndex == other.StartIndex && EndIndex == other.EndIndex) {
                return JumpToIndex < other.JumpToIndex;
            }

            if (StartIndex > other.StartIndex) {
                Debug.Assert(EndIndex <= other.EndIndex);
                return true;
            } else if (EndIndex < other.EndIndex) {
                Debug.Assert(StartIndex == other.StartIndex);
                return true;
            } else {
                return false;
            }
        }
    }

    public class DebugInfo {
        public int StartLine, EndLine;
        public int StartIndex, EndIndex;
        public string FileName;

        public bool Matches(int index) {
            return index >= StartIndex && index < EndIndex;
        }
    }
    
    public class LightCompiler {
        private List<Instruction> _instructions = new List<Instruction>();
        private int _maxStackDepth = 0;
        private int _currentStackDepth = 0;

        private List<ParameterExpression> _locals = new List<ParameterExpression>();
        private List<bool> _localIsBoxed = new List<bool>();
        private List<ParameterExpression> _closureVariables = new List<ParameterExpression>();

        private List<ExceptionHandler> _handlers = new List<ExceptionHandler>();
        private List<DebugInfo> _debugInfos = new List<DebugInfo>();

        private Dictionary<LabelTarget, Label> _labels = new Dictionary<LabelTarget, Label>();

        private Stack<ParameterExpression> _exceptionForRethrowStack = new Stack<ParameterExpression>();
        private Stack<FinallyLabels> _finallyLabels = new Stack<FinallyLabels>();

        private LightCompiler _parent;

        internal LightCompiler() {}

        private LightCompiler(LightCompiler parent) : this() {
            this._parent = parent;
        }

        internal Interpreter CompileTop(LambdaExpression node) {
            foreach (var p in node.Parameters) {
                this.AddVariable(p);
            }
            
            this.Compile(node.Body);
            return this.MakeInterpreter(node);
        }


        private Interpreter MakeInterpreter(LambdaExpression lambda) {
            var handlers = _handlers.ToArray();
            var debugInfos = _debugInfos.ToArray();
            return new Interpreter(lambda, _localIsBoxed.ToArray(), _maxStackDepth, _instructions.ToArray(), handlers, debugInfos);
        }

        class FinallyLabels {
            public Label startOfFinally;
            public Dictionary<Label, List<OffsetInstruction>> labels = 
                new Dictionary<Label, List<OffsetInstruction>>();
            public Dictionary<Label, bool> labelHasValue = new Dictionary<Label, bool>();

            public FinallyLabels(Label startOfFinally) {
                this.startOfFinally = startOfFinally;
            }

            public void AddBranch(OffsetInstruction instruction, Label label, bool hasValue) {
                List<OffsetInstruction> branches;
                if (!labels.TryGetValue(label, out branches)) {
                    branches = new List<OffsetInstruction>();
                    labels[label] = branches;
                    labelHasValue[label] = hasValue;
                }
                Debug.Assert(labelHasValue[label] == hasValue);
                branches.Add(instruction);
            }
        }

        class Label {
            public int _index;
            private LightCompiler _compiler;
            public int _expectedStackSize = -1;
            private List<OffsetInstruction> _offsetInstructions = new List<OffsetInstruction>();

            public Label(LightCompiler compiler) {
                this._compiler = compiler;
                this._index = -1;
            }

            public void NoteStackSize() {
                int stackSize = _compiler._currentStackDepth;
                if (_expectedStackSize == -1) _expectedStackSize = stackSize;
                Debug.Assert(_expectedStackSize == stackSize);
            }

            public void Mark() {
                if (_expectedStackSize != -1) {
                    Debug.Assert(_compiler._currentStackDepth == -1 || _compiler._currentStackDepth == _expectedStackSize);
                    _compiler._currentStackDepth = _expectedStackSize;
                } else {
                    _expectedStackSize = _compiler._currentStackDepth;
                }

                this._index = _compiler._instructions.Count;
                foreach (var oi in _offsetInstructions) {
                    SetOffset(oi);
                }
            }

            public void RemoveBinding(OffsetInstruction instruction) {
                this._offsetInstructions.Remove(instruction);
            }

            public void SetOffset(OffsetInstruction instruction) {
                //TODO some work here to verify expectedStackSize
                if (_index == -1) {
                    this._offsetInstructions.Add(instruction);
                } else {
                    int index = _compiler._instructions.IndexOf(instruction);
                    int offset = _index - index;
                    instruction.SetOffset(offset);
                }
            }
        }

        private Label MakeLabel() {
            return new Label(this);
        }

        private Label ReferenceLabel(LabelTarget target) {
            Label ret;
            if (!_labels.TryGetValue(target, out ret)) {
                ret = MakeLabel();
                _labels[target] = ret;
            }
            return ret;
        }

        private ExceptionHandler AddHandler(Type exceptionType, ParameterExpression exceptionParameter, int start, int end) {
            var handler = new ExceptionHandler() { 
                ExceptionType = exceptionType, PushException = exceptionParameter != null,
                StartIndex = start, EndIndex = end,
                JumpToIndex = _instructions.Count };
            _handlers.Add(handler);
            return handler;
        }

        private BranchInstruction AddBranch(Label label) {
            var branch = new BranchInstruction();
            AddBranch(branch, label);
            _currentStackDepth = -1; // always clear the stack after an unconditional branch
            return branch;
        }

        private void AddBranch(OffsetInstruction instruction, Label label) {
            if (_currentStackDepth == -1) {
                return; // this code is unreachable
            }

            AddInstruction(instruction);
            label.NoteStackSize();
            label.SetOffset(instruction);
        }

        public void AddInstruction(Instruction instruction) {
            //Debug.Assert(_currentStackDepth >= 0); // checks that the stack is valid
            if (_currentStackDepth == -1) {
                return; // this code is unreachable
            }

            _instructions.Add(instruction);
            _currentStackDepth -= instruction.ConsumedStack;
            Debug.Assert(_currentStackDepth >= 0); // checks that there's enough room to pop
            _currentStackDepth += instruction.ProducedStack;
            if (_currentStackDepth > _maxStackDepth) _maxStackDepth = _currentStackDepth;
        }

        public void PushConstant(object value) {
            AddInstruction(new PushInstruction(value));
        }

        private void CompileConstantExpression(Expression expr) {
            var node = (ConstantExpression)expr;

            PushConstant(node.Value);
        }

        private void CompileDefaultExpression(Expression expr) {
            if (_currentStackDepth == -1) {
                // HEURISTIC this means this is unreachable code and not needed to be compiled
                return;
            }

            var node = (DefaultExpression)expr;
            if (node.Type != typeof(void)) {
                object value;
                if (node.Type.IsValueType) {
                    value = Activator.CreateInstance(node.Type);
                } else {
                    value = null;
                }
                PushConstant(value);
            }
        }

        private bool IsBoxed(int index) {
            return _localIsBoxed[index];
        }

        private void SwitchToBoxed(int index) {
            for (int i = 0; i < _instructions.Count; i++) {
                var instruction = _instructions[i] as IBoxableInstruction;

                if (instruction != null) {
                    var newInstruction = instruction.BoxIfIndexMatches(index);
                    if (newInstruction != null) {
                        _instructions[i] = newInstruction;
                    }
                }
            }
        }

        private void EnsureAvailableForClosure(ParameterExpression expr) {
            int index = _locals.IndexOf(expr);
            if (index != -1) {
                if (!_localIsBoxed[index]) {
                    _localIsBoxed[index] = true;
                    SwitchToBoxed(index);
                }
                return;
            }

            index = _closureVariables.IndexOf(expr);
            if (index == -1) {
                _parent.EnsureAvailableForClosure(expr);
            }
            _closureVariables.Add(expr);
        }

        private Instruction GetVariable(ParameterExpression expr) {
            int index = _locals.IndexOf(expr);
            if (index != -1) {
                if (_localIsBoxed[index]) {
                    return new GetBoxedLocalInstruction(index);
                } else {
                    return new GetLocalInstruction(index);
                }
            }

            this.EnsureAvailableForClosure(expr);

            index = _closureVariables.IndexOf(expr);
            Debug.Assert(index != -1);
            return new GetClosureInstruction(index);
        }

        private Instruction GetBoxedVariable(ParameterExpression expr) {
            int index = _locals.IndexOf(expr);
            if (index != -1) {
                Debug.Assert(_localIsBoxed[index]);
                return new GetLocalInstruction(index);
            }

            this.EnsureAvailableForClosure(expr);

            index = _closureVariables.IndexOf(expr);
            Debug.Assert(index != -1);
            return new GetBoxedClosureInstruction(index);
        }

        private void SetVariable(ParameterExpression expr, bool isVoid) {
            int index = _locals.IndexOf(expr);
            if (index != -1) {
                if (_localIsBoxed[index]) {
                    if (isVoid) AddInstruction(new SetBoxedLocalVoidInstruction(index));
                    else AddInstruction(new SetBoxedLocalInstruction(index));
                } else {
                    if (isVoid) AddInstruction(new SetLocalVoidInstruction(index));
                    else AddInstruction(new SetLocalInstruction(index));
                }
                return;
            }

            this.EnsureAvailableForClosure(expr);

            index = _closureVariables.IndexOf(expr);
            Debug.Assert(index != -1);
            AddInstruction(new SetClosureInstruction(index));
            if (isVoid) AddInstruction(PopInstruction.Instance);
        }


        private void AddVariable(ParameterExpression expr) {
            _locals.Add(expr);
            _localIsBoxed.Add(false);
        }

        private void CompileParameterExpression(Expression expr) {
            var node = (ParameterExpression)expr;
            AddInstruction(GetVariable(node));
        }


        private void CompileBlockExpression(Expression expr, bool asVoid) {
            var node = (BlockExpression)expr;

            //TODO pop these off a stack when exiting
            foreach (var local in node.Variables) {
                this.AddVariable(local);
            }

            for (int i = 0; i < node.Expressions.Count - 1; i++) {
                this.CompileAsVoid(node.Expressions[i]);
            }
            var lastExpression = node.Expressions[node.Expressions.Count - 1];
            if (asVoid) {
                this.CompileAsVoid(lastExpression);
            } else {
                this.Compile(lastExpression);
            }
        }

        private void CompileIndexAssignment(BinaryExpression node, bool asVoid) {
            throw new NotImplementedException();
        }

        private void CompileMemberAssignment(BinaryExpression node, bool asVoid) {
            var member = (MemberExpression)node.Left;

            PropertyInfo pi = member.Member as PropertyInfo;
            if (pi != null) {
                var method = pi.GetSetMethod();
                this.Compile(member.Expression);
                if (!asVoid) {
                    // need to dup node.Right correctly with a temp
                    throw new NotImplementedException();
                }
                this.Compile(node.Right);
                AddInstruction(new CallInstruction(method));
                return;
            }


            throw new NotImplementedException();
        }

        private void CompileVariableAssignment(BinaryExpression node, bool asVoid) {
            this.Compile(node.Right);

            var target = (ParameterExpression)node.Left;
            SetVariable(target, asVoid);
        }

        private void CompileAssignBinaryExpression(Expression expr, bool asVoid) {
            var node = (BinaryExpression)expr;

            switch (node.Left.NodeType) {
                case ExpressionType.Index:
                    CompileIndexAssignment(node, asVoid); break;
                case ExpressionType.MemberAccess:
                    CompileMemberAssignment(node, asVoid); break;
                case ExpressionType.Parameter:
                case ExpressionType.Extension:
                    CompileVariableAssignment(node, asVoid); break;
                default:
                    throw new InvalidOperationException("Invalid lvalue for assignment: " + node.Left.NodeType);
            }
        }

        private void CompileBinaryExpression(Expression expr) {
            var node = (BinaryExpression)expr;

            if (node.Method != null) {
                this.Compile(node.Left);
                this.Compile(node.Right);
                AddInstruction(new CallInstruction(node.Method));
            } else {
                switch (node.NodeType) {
                    case ExpressionType.ArrayIndex:
                        CompileArrayIndex(node.Left, node.Right);
                        return;
                    case ExpressionType.Equal:
                        CompileEqual(node.Left, node.Right);
                        return;
                    case ExpressionType.NotEqual:
                        CompileNotEqual(node.Left, node.Right);
                        return;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private void CompileEqual(Expression left, Expression right) {
            if (left.Type == typeof(bool) && right.Type == typeof(bool)) {
                this.Compile(left);
                this.Compile(right);
                AddInstruction(EqualBoolInstruction.Instance);
            } else if (left.Type == typeof(int) && right.Type == typeof(int)) {
                this.Compile(left);
                this.Compile(right);
                AddInstruction(EqualIntInstruction.Instance);
            } else {
                throw new NotImplementedException();
            }
        }

        private void CompileNotEqual(Expression left, Expression right) {
            if (left.Type == typeof(int) && right.Type == typeof(int)) {
                this.Compile(left);
                this.Compile(right);
                AddInstruction(NotEqualIntInstruction.Instance);
            } else if (!left.Type.IsValueType) {
                this.Compile(left);
                this.Compile(right);
                AddInstruction(NotEqualObjectInstruction.Instance);
            } else {
                throw new NotImplementedException();
            }
        }


        private void CompileArrayIndex(Expression array, Expression index) {
            if (array.Type == typeof(object[]) && index.Type == typeof(int)) {
                this.Compile(array);
                this.Compile(index);
                AddInstruction(ArrayIndexInstruction<object>.Instance);
            } else {
                throw new NotImplementedException();
            }
        }


        private void CompileIndexExpression(Expression expr) {
            var node = (IndexExpression)expr;

            if (node.Object.Type.IsArray && node.Arguments.Count == 1) {
                CompileArrayIndex(node.Object, node.Arguments[0]);
                return;
            }

            throw new System.NotImplementedException();
        }


        private void CompileConvertUnaryExpression(Expression expr) {
            var node = (UnaryExpression)expr;

            if (expr.Type == typeof(void)) {
                this.CompileAsVoid(node.Operand);
            } else {
                this.Compile(node.Operand);
                //TODO check the logic on this, but we think we can ignore conversions in this boxed world
            }
        }

        private void CompileNotExpression(UnaryExpression node) {
            if (node.Operand.Type == typeof(bool)) {
                this.Compile(node.Operand);
                AddInstruction(NotInstruction.Instance);
            } else {
                throw new NotImplementedException();
            }
        }

        private void CompileUnaryExpression(Expression expr) {
            var node = (UnaryExpression)expr;
            
            if (node.Method != null) {
                this.Compile(node.Operand);
                AddInstruction(new CallInstruction(node.Method));
            } else {
                switch (node.NodeType) {
                    case ExpressionType.Not:
                        CompileNotExpression(node);
                        return;
                    default:
                        throw new NotImplementedException();
                }
            }
        }


        private void CompileAndAlsoBinaryExpression(Expression expr) {
            var node = (BinaryExpression)expr;

            if (node.Left.Type == typeof(bool)) {
                this.Compile(node.Left);
                var elseLabel = MakeLabel();
                var endLabel = MakeLabel();
                AddBranch(new BranchFalseInstruction(), elseLabel);
                this.Compile(node.Right);
                AddBranch(endLabel);
                elseLabel.Mark();
                PushConstant(false);
                endLabel.Mark();
                return;
            }
            
            throw new System.NotImplementedException();
        }


        private void CompileConditionalExpression(Expression expr) {
            var node = (ConditionalExpression)expr;
            this.Compile(node.Test);

            if (node.IfTrue == Expression.Empty()) {
                var endOfFalse = MakeLabel();
                AddBranch(new BranchTrueInstruction(), endOfFalse);
                this.Compile(node.IfFalse);
                endOfFalse.Mark();
            } else {
                var endOfTrue = MakeLabel();
                AddBranch(new BranchFalseInstruction(), endOfTrue);
                this.Compile(node.IfTrue);

                if (node.IfFalse != Expression.Empty()) {
                    var endOfFalse = MakeLabel();
                    AddBranch(endOfFalse);
                    endOfTrue.Mark();
                    this.Compile(node.IfFalse);
                    endOfFalse.Mark();
                } else {
                    endOfTrue.Mark();
                }
            }
        }

        private void CompileLoopExpression(Expression expr) {
            var node = (LoopExpression)expr;

            var continueLabel = node.ContinueLabel == null ? 
                        MakeLabel() : ReferenceLabel(node.ContinueLabel);

            continueLabel.Mark();
            this.Compile(node.Body);
            if (node.Body.Type != typeof(void)) {
                AddInstruction(PopInstruction.Instance);
            }
            AddBranch(continueLabel);

            if (node.BreakLabel != null) {
                ReferenceLabel(node.BreakLabel).Mark();
            }
        }

        private void CompileSwitchExpression(Expression expr) {
            var node = (SwitchExpression)expr;

            if (node.Test.Type != typeof(int)) throw new NotImplementedException();

            Debug.Assert(node.Type == typeof(void));

            this.Compile(node.Test);
            int start = _instructions.Count;
            var switchInstruction = new SwitchInstruction();
            AddInstruction(switchInstruction);
            bool setDefault = false;
            int switchStack = _currentStackDepth;
            foreach (var clause in node.SwitchCases) {
                _currentStackDepth = switchStack;
                int offset = _instructions.Count - start;
                if (clause.IsDefault) {
                    setDefault = true;
                    switchInstruction.AddDefault(offset);
                } else {
                    switchInstruction.AddCase(clause.Value, offset);
                }
                this.Compile(clause.Body);
                Debug.Assert(_currentStackDepth == -1 || _currentStackDepth == switchStack);
            }
            if (!setDefault) {
                switchInstruction.AddDefault(_instructions.Count - start);
                _currentStackDepth = switchStack;
            }

            if (node.BreakLabel != null) {
                ReferenceLabel(node.BreakLabel).Mark();
            }
        }

        private void CompileLabelExpression(Expression expr) {
            var node = (LabelExpression)expr;

            if (node.DefaultValue != null) {
                this.Compile(node.DefaultValue);
            }

            ReferenceLabel(node.Target).Mark();
        }

        private void CompileGotoExpression(Expression expr) {
            var node = (GotoExpression)expr;

            //int finalStackDepth = _currentStackDepth;
            if (node.Value != null) {
                this.Compile(node.Value);
            }

            var label = ReferenceLabel(node.Target);
            var branch = AddBranch(label);

            // goto is the only node that can break out of a finally
            // so we do this here instead of in the AddBranch method
            if (_finallyLabels.Count > 0) {
                var labels = _finallyLabels.Peek();
                labels.AddBranch(branch, label, node.Value != null);
            }

            //_currentStackDepth = finalStackDepth;
        }

        private void CompileThrowUnaryExpression(Expression expr, bool asVoid) {
            Debug.Assert(asVoid);
            var node = (UnaryExpression)expr;

            if (node.Operand == null) {
                AddInstruction(GetVariable(_exceptionForRethrowStack.Peek()));
                AddInstruction(RethrowInstruction.Instance);
            } else {
                this.Compile(node.Operand);
                AddInstruction(ThrowInstruction.Instance);
            }
            //TODO _currentStackDepth = -1;
        }

        //TODO this needs to also check that there are no jumps outside of expr (including returns)
        private bool EndsWithRethrow(Expression expr) {
            if (expr.NodeType == ExpressionType.Throw) {
                var node = (UnaryExpression)expr;
                return node.Operand == null;
            }

            BlockExpression block = expr as BlockExpression;
            if (block != null) {
                return EndsWithRethrow(block.Expressions[block.Expressions.Count - 1]);
            }
            return false;
        }

        private void CompileWithoutRethrow(Expression expr) {
            if (expr.NodeType == ExpressionType.Throw) {
                var throwNode = (UnaryExpression)expr;
                Debug.Assert(throwNode.Operand == null);
                return;
            }

            BlockExpression node = (BlockExpression)expr;
            foreach (var local in node.Variables) {
                this.AddVariable(local);
            }


            for (int i = 0; i < node.Expressions.Count - 1; i++) {
                this.Compile(node.Expressions[i]);
                if (node.Expressions[i].Type != typeof(void)) {
                    AddInstruction(PopInstruction.Instance);
                }
            }

            CompileWithoutRethrow(node.Expressions[node.Expressions.Count - 1]);
        }


        private void CompileTryExpression(Expression expr) {
            var node = (TryExpression)expr;

            Label startOfFinally = MakeLabel();

            if (node.Finally != null) {
                _finallyLabels.Push(new FinallyLabels(startOfFinally));
            }

            int startingStack = _currentStackDepth;


            int start = _instructions.Count;
            this.Compile(node.Body);
            int end = _instructions.Count;


            if (_currentStackDepth != -1) {
                AddBranch(startOfFinally);
            }

            if (node.Finally == null && node.Fault == null && node.Handlers.Count == 1) {
                var handler = node.Handlers[0];
                if (handler.Filter == null && handler.Test == typeof(Exception) && handler.Variable == null) {
                    if (EndsWithRethrow(handler.Body)) {
                        var fault = this.AddHandler(null, null, start, end);
                        _currentStackDepth = startingStack;
                        CompileWithoutRethrow(handler.Body);
                        fault.EndHandlerIndex = this._instructions.Count;
                        startOfFinally.Mark();
                        return;
                    }
                }
            }

            foreach (var handler in node.Handlers) {
                if (handler.Filter != null) throw new NotImplementedException();
                var parameter = handler.Variable;

                // TODO we should only create one of these if needed for a rethrow
                if (parameter == null) {
                    parameter = Expression.Parameter(handler.Test, "currentException");
                    AddVariable(parameter);
                }
                this.AddHandler(handler.Test, parameter, start, end);

                _exceptionForRethrowStack.Push(parameter);
                _currentStackDepth = startingStack + 1;
                SetVariable(parameter, true);
                
                this.Compile(handler.Body);

                //TODO pop this scoped variable that we no longer need
                //PopVariable(parameter);
                _exceptionForRethrowStack.Pop();

                AddBranch(startOfFinally);
            }
            
            if (node.Fault != null) {
                throw new NotImplementedException();
            }

            if (node.Finally != null) {
                var myLabels = _finallyLabels.Pop();
                var myNewTargets = new List<Label>();

                int finallyStart = _instructions.Count;
                ParameterExpression finallyStateVar = null;
                ParameterExpression finallyStackValue = null;

                foreach (var kv in myLabels.labels) {
                    var label = kv.Key;
                    if (label._index == -1 || label._index < start || label._index > finallyStart) {
                        myNewTargets.Add(label);
                        var currentLabel = MakeLabel();
                        _currentStackDepth = -1;
                        currentLabel._expectedStackSize = label._expectedStackSize;
                        currentLabel.Mark();
                        foreach (var branch in kv.Value) {
                            currentLabel.SetOffset(branch);
                            label.RemoveBinding(branch);
                        }
                        if (finallyStateVar == null) {
                            finallyStateVar = Expression.Parameter(typeof(int), "finallyBranch");
                            AddVariable(finallyStateVar);
                            finallyStackValue = Expression.Parameter(typeof(object), "stackValue");
                            AddVariable(finallyStackValue);
                        }
                        if (myLabels.labelHasValue[label]) {
                            SetVariable(finallyStackValue, true);
                        }
                        PushConstant(myNewTargets.Count-1);
                        SetVariable(finallyStateVar, true);

                        AddBranch(startOfFinally);
                    }
                }

                _currentStackDepth = startingStack + ((node.Body.Type == typeof(void)) ? 0 : 1);

                startOfFinally.Mark();
                var faultHandler = this.AddHandler(null, null, start, end);
                this.Compile(node.Finally);
                if (node.Finally.Type != typeof(void)) {
                    AddInstruction(PopInstruction.Instance);
                }
                faultHandler.EndHandlerIndex = _instructions.Count;

                if (finallyStateVar != null) {
                    // we can make this much more efficient in the future
                    var si = new SwitchInstruction();
                    AddInstruction(GetVariable(finallyStateVar));

                    int switchIndex = _instructions.Count;
                    AddInstruction(si);
                    int switchStack = _currentStackDepth;
                    for (int i = 0; i < myNewTargets.Count; i++) {
                        _currentStackDepth = switchStack;
                        si.AddCase(i, _instructions.Count-switchIndex);
                        if (myLabels.labelHasValue[myNewTargets[i]]) {
                            AddInstruction(GetVariable(finallyStackValue));
                        }
                        var branchInstruction = AddBranch(myNewTargets[i]);
                        if (_finallyLabels.Count > 0) {
                            var labels = _finallyLabels.Peek();
                            labels.AddBranch(branchInstruction, myNewTargets[i], myLabels.labelHasValue[myNewTargets[i]]);
                        }
                    }
                    si.AddDefault(_instructions.Count - switchIndex);
                    _currentStackDepth = switchStack; // we might exit totally normally!
                }
            } else {
                startOfFinally.Mark();
            }

        }

        private void CompileDynamicExpression(Expression expr) {
            var node = (DynamicExpression)expr;

            foreach (var arg in node.Arguments) {
                this.Compile(arg);
            }

            AddInstruction(DynamicInstructions.MakeInstruction(node.DelegateType, node.Binder));
        }

        private void CompileMethodCallExpression(Expression expr) {
            var node = (MethodCallExpression)expr;
            //TODO support pass by reference and lots of other fancy stuff

            if (!node.Method.IsStatic) {
                this.Compile(node.Object);
            }

            foreach (var arg in node.Arguments) {
                this.Compile(arg);
            }

            AddInstruction(new CallInstruction(node.Method));
        }

        private bool TryConstantFold(IList<Expression> argExpressions, out object[] args) {
            args = new object[argExpressions.Count];
            for (int i = 0; i < args.Length; i++) {
                ConstantExpression ce = argExpressions[i] as ConstantExpression;
                if (ce == null) return false;
                args[i] = ce.Value;
            }
            return true;
        }


        private void CompileNewExpression(Expression expr) {
            var node = (NewExpression)expr;
            object[] args;
            //TODO this reflects poorly on the incoming tree
            if (TryConstantFold(node.Arguments, out args)) {
                object value = node.Constructor.Invoke(args);
                PushConstant(value);
                return;
            }

            foreach (var arg in node.Arguments) {
                this.Compile(arg);
            }
            AddInstruction(new NewInstruction(node.Constructor));

        }

        private void CompileMemberExpression(Expression expr) {
            var node = (MemberExpression)expr;

            var member = node.Member;
            FieldInfo fi = member as FieldInfo;
            if (fi != null) {
                if (fi.IsInitOnly && fi.IsStatic) {
                    object value = fi.GetValue(null);
                    PushConstant(value);
                    return;
                }
            }

            PropertyInfo pi = member as PropertyInfo;
            if (pi != null) {
                var method = pi.GetGetMethod();
                this.Compile(node.Expression);
                AddInstruction(new CallInstruction(method));
                return;
            }


            throw new System.NotImplementedException();
        }

        private void CompileNewArrayExpression(Expression expr) {
            var node = (NewArrayExpression)expr;
            object[] args;
            //TODO this reflects poorly on the incoming tree
            if (TryConstantFold(node.Expressions, out args)) {
                Array array;
                if (node.NodeType == ExpressionType.NewArrayInit) {
                    array = Array.CreateInstance(node.Type.GetElementType(), args.Length);
                    for (int i = 0; i < args.Length; i++) {
                        array.SetValue(args[i], i);
                    }
                } else if (node.NodeType == ExpressionType.NewArrayBounds) {
                    if (args.Length != 1) throw new NotImplementedException();
                    array = Array.CreateInstance(node.Type.GetElementType(), (int)args[0]);
                } else {
                    throw new NotImplementedException();
                }
                PushConstant(array);
                return;
            }

            if (node.NodeType == ExpressionType.NewArrayInit) {
                foreach (var arg in node.Expressions) {
                    this.Compile(arg);
                }
                AddInstruction(new NewArrayInstruction(node.Type.GetElementType(), node.Expressions.Count));
                return;
            }

            throw new System.NotImplementedException();
        }

        private void CompileExtensionExpression(Expression expr) {
            var instructionProvider = expr as IInstructionProvider;
            if (instructionProvider != null) {
                AddInstruction(instructionProvider.GetInstruction(this));
                return;
            }

            var node = expr as Microsoft.Scripting.Ast.SymbolConstantExpression;
            if (node != null) {
                PushConstant(node.Value);
                return;
            }

            if (expr.CanReduce) {
                Compile(expr.Reduce());
            } else {
                throw new System.NotImplementedException();
            }
        }


        private void CompileDebugInfoExpression(Expression expr) {
            var node = (DebugInfoExpression)expr;
            int start = _instructions.Count;
            this.Compile(node.Expression);
            int end = _instructions.Count;
            var info = new DebugInfo()
            {
                StartIndex = start,
                EndIndex = end,
                FileName = node.Document.FileName,
                StartLine = node.StartLine,
                EndLine = node.EndLine
            };
            _debugInfos.Add(info);
        }

        private void CompileRuntimeVariablesExpression(Expression expr) {
            //Generates an IList<IStrongBox> for all requested variables
            var node = (RuntimeVariablesExpression)expr;
            foreach (var variable in node.Variables) {
                this.EnsureAvailableForClosure(variable);
                AddInstruction(GetBoxedVariable(variable));
            }

            AddInstruction(new RuntimeVariablesInstruction(node.Variables.Count));
        }


        private void CompileLambdaExpression(Expression expr) {
            var node = (LambdaExpression)expr;
            var compiler = new LightCompiler(this);
            var interpreter = compiler.CompileTop(node);

            int[] closureBoxes = new int[compiler._closureVariables.Count];
            for (int i = 0; i < closureBoxes.Length; i++) {
                var closureVar = compiler._closureVariables[i];
                AddInstruction(GetBoxedVariable(closureVar));
            }
            AddInstruction(new CreateDelegateInstruction(node.Type, interpreter, compiler._closureVariables.Count));
        }

        private void CompileCoalesceBinaryExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileInvocationExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileListInitExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileMemberInitExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileOrElseBinaryExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileQuoteUnaryExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileUnboxUnaryExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileTypeBinaryExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileReducibleExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        internal void CompileAsVoid(Expression expr) {
            switch (expr.NodeType) {
                case ExpressionType.Assign:
                    CompileAssignBinaryExpression(expr, true);
                    break;
                case ExpressionType.Block:
                    CompileBlockExpression(expr, true);
                    break;
                case ExpressionType.Throw:
                    CompileThrowUnaryExpression(expr, true);
                    break;
                case ExpressionType.Constant:
                case ExpressionType.Default:
                case ExpressionType.Parameter:
                    // no-op
                    break;
                default:
                    Compile(expr);
                    if (expr.Type != typeof(void)) {
                        AddInstruction(PopInstruction.Instance);
                    }
                    break;
            }
        }


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        internal void Compile(Expression expr) {
            int startingStackDepth = this._currentStackDepth;
            switch (expr.NodeType) {
                case ExpressionType.Add: CompileBinaryExpression(expr); break;
                case ExpressionType.AddChecked: CompileBinaryExpression(expr); break;
                case ExpressionType.And: CompileBinaryExpression(expr); break;
                case ExpressionType.AndAlso: CompileAndAlsoBinaryExpression(expr); break;
                case ExpressionType.ArrayLength: CompileUnaryExpression(expr); break;
                case ExpressionType.ArrayIndex: CompileBinaryExpression(expr); break;
                case ExpressionType.Call: CompileMethodCallExpression(expr); break;
                case ExpressionType.Coalesce: CompileCoalesceBinaryExpression(expr); break;
                case ExpressionType.Conditional: CompileConditionalExpression(expr); break;
                case ExpressionType.Constant: CompileConstantExpression(expr); break;
                case ExpressionType.Convert: CompileConvertUnaryExpression(expr); break;
                case ExpressionType.ConvertChecked: CompileConvertUnaryExpression(expr); break;
                case ExpressionType.Divide: CompileBinaryExpression(expr); break;
                case ExpressionType.Equal: CompileBinaryExpression(expr); break;
                case ExpressionType.ExclusiveOr: CompileBinaryExpression(expr); break;
                case ExpressionType.GreaterThan: CompileBinaryExpression(expr); break;
                case ExpressionType.GreaterThanOrEqual: CompileBinaryExpression(expr); break;
                case ExpressionType.Invoke: CompileInvocationExpression(expr); break;
                case ExpressionType.Lambda: CompileLambdaExpression(expr); break;
                case ExpressionType.LeftShift: CompileBinaryExpression(expr); break;
                case ExpressionType.LessThan: CompileBinaryExpression(expr); break;
                case ExpressionType.LessThanOrEqual: CompileBinaryExpression(expr); break;
                case ExpressionType.ListInit: CompileListInitExpression(expr); break;
                case ExpressionType.MemberAccess: CompileMemberExpression(expr); break;
                case ExpressionType.MemberInit: CompileMemberInitExpression(expr); break;
                case ExpressionType.Modulo: CompileBinaryExpression(expr); break;
                case ExpressionType.Multiply: CompileBinaryExpression(expr); break;
                case ExpressionType.MultiplyChecked: CompileBinaryExpression(expr); break;
                case ExpressionType.Negate: CompileUnaryExpression(expr); break;
                case ExpressionType.UnaryPlus: CompileUnaryExpression(expr); break;
                case ExpressionType.NegateChecked: CompileUnaryExpression(expr); break;
                case ExpressionType.New: CompileNewExpression(expr); break;
                case ExpressionType.NewArrayInit: CompileNewArrayExpression(expr); break;
                case ExpressionType.NewArrayBounds: CompileNewArrayExpression(expr); break;
                case ExpressionType.Not: CompileUnaryExpression(expr); break;
                case ExpressionType.NotEqual: CompileBinaryExpression(expr); break;
                case ExpressionType.Or: CompileBinaryExpression(expr); break;
                case ExpressionType.OrElse: CompileOrElseBinaryExpression(expr); break;
                case ExpressionType.Parameter: CompileParameterExpression(expr); break;
                case ExpressionType.Power: CompileBinaryExpression(expr); break;
                case ExpressionType.Quote: CompileQuoteUnaryExpression(expr); break;
                case ExpressionType.RightShift: CompileBinaryExpression(expr); break;
                case ExpressionType.Subtract: CompileBinaryExpression(expr); break;
                case ExpressionType.SubtractChecked: CompileBinaryExpression(expr); break;
                case ExpressionType.TypeAs: CompileUnaryExpression(expr); break;
                case ExpressionType.TypeIs: CompileTypeBinaryExpression(expr); break;
                case ExpressionType.Assign: CompileAssignBinaryExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.Block: CompileBlockExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.DebugInfo: CompileDebugInfoExpression(expr); break;
                case ExpressionType.Decrement: CompileUnaryExpression(expr); break;
                case ExpressionType.Dynamic: CompileDynamicExpression(expr); break;
                case ExpressionType.Default: CompileDefaultExpression(expr); break;
                case ExpressionType.Extension: CompileExtensionExpression(expr); break;
                case ExpressionType.Goto: CompileGotoExpression(expr); break;
                case ExpressionType.Increment: CompileUnaryExpression(expr); break;
                case ExpressionType.Index: CompileIndexExpression(expr); break;
                case ExpressionType.Label: CompileLabelExpression(expr); break;
                case ExpressionType.RuntimeVariables: CompileRuntimeVariablesExpression(expr); break;
                case ExpressionType.Loop: CompileLoopExpression(expr); break;
                case ExpressionType.Switch: CompileSwitchExpression(expr); break;
                case ExpressionType.Throw: CompileThrowUnaryExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.Try: CompileTryExpression(expr); break;
                case ExpressionType.Unbox: CompileUnboxUnaryExpression(expr); break;
                case ExpressionType.TypeEqual: CompileTypeBinaryExpression(expr); break;
                case ExpressionType.OnesComplement: CompileUnaryExpression(expr); break;
                case ExpressionType.IsTrue: CompileUnaryExpression(expr); break;
                case ExpressionType.IsFalse: CompileUnaryExpression(expr); break;
                case ExpressionType.AddAssign:
                case ExpressionType.AndAssign:
                case ExpressionType.DivideAssign:
                case ExpressionType.ExclusiveOrAssign:
                case ExpressionType.LeftShiftAssign:
                case ExpressionType.ModuloAssign:
                case ExpressionType.MultiplyAssign:
                case ExpressionType.OrAssign:
                case ExpressionType.PowerAssign:
                case ExpressionType.RightShiftAssign:
                case ExpressionType.SubtractAssign:
                case ExpressionType.AddAssignChecked:
                case ExpressionType.MultiplyAssignChecked:
                case ExpressionType.SubtractAssignChecked:
                case ExpressionType.PreIncrementAssign:
                case ExpressionType.PreDecrementAssign:
                case ExpressionType.PostIncrementAssign:
                case ExpressionType.PostDecrementAssign:
                    CompileReducibleExpression(expr); break;
                default: throw Assert.Unreachable;
            };
            Debug.Assert(_currentStackDepth == -1 || startingStackDepth == -1 ||
                _currentStackDepth == startingStackDepth + (expr.Type == typeof(void) ? 0 : 1));
        }


    }
}
