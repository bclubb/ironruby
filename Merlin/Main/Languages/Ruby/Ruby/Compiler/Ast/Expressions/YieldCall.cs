/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * ironruby@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System.Diagnostics;
using Microsoft.Scripting;
using IronRuby.Builtins;
using IronRuby.Runtime;
using AstUtils = Microsoft.Scripting.Ast.Utils;
using MSA = System.Linq.Expressions;

namespace IronRuby.Compiler.Ast {
    using Ast = System.Linq.Expressions.Expression;

    /// <summary>
    /// yield(args)
    /// </summary>
    public partial class YieldCall : CallExpression {

        public YieldCall(Arguments args, SourceSpan location)
            : base(args, null, location) {
        }

        // see Ruby Language.doc/Runtime/Control Flow Implementation/Yield
        internal override MSA.Expression/*!*/ TransformRead(AstGenerator/*!*/ gen) {
            MSA.Expression bfcVariable = gen.CurrentScope.DefineHiddenVariable("#yielded-bfc", typeof(BlockParam));
            MSA.Expression resultVariable = gen.CurrentScope.DefineHiddenVariable("#result", typeof(object));
            MSA.Expression evalUnwinder = gen.CurrentScope.DefineHiddenVariable("#unwinder", typeof(EvalUnwinder));

            MSA.Expression postYield;

            if (gen.CompilerOptions.IsEval) {
                // eval:
                postYield = Methods.EvalYield.OpCall(gen.CurrentRfcVariable, bfcVariable, resultVariable);
            } else if (gen.CurrentBlock != null) {
                // block:
                postYield = Methods.BlockYield.OpCall(gen.CurrentRfcVariable, gen.CurrentBlock.BfcVariable, bfcVariable, resultVariable);
            } else {
                // method:
                postYield = Methods.MethodYield.OpCall(gen.CurrentRfcVariable, bfcVariable, resultVariable);
            }

            return AstFactory.Block(
                gen.DebugMarker("#RB: yield begin"),

                Ast.Assign(bfcVariable, Methods.CreateBfcForYield.OpCall(gen.MakeMethodBlockParameterRead())),

                Ast.Assign(resultVariable, (Arguments ?? Arguments.Empty).TransformToYield(gen, bfcVariable,
                    Ast.Property(AstUtils.Convert(gen.MakeMethodBlockParameterRead(), typeof(Proc)), Proc.SelfProperty)
                )),

                AstUtils.IfThen(postYield, gen.Return(resultVariable)),

                gen.DebugMarker("#RB: yield end"),

                resultVariable
            );
        }

        internal override string/*!*/ GetNodeName(AstGenerator/*!*/ gen) {
            return "yield";
        }

        internal override MSA.Expression TransformDefinedCondition(AstGenerator/*!*/ gen) {
            // block_given semantics:
            return Ast.NotEqual(gen.MakeMethodBlockParameterRead(), Ast.Constant(null));
        }

    }
}
