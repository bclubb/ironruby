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

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Dynamic.Utils;

namespace System.Linq.Expressions.Compiler {
    partial class LambdaCompiler {

        private void EmitQuoteUnaryExpression(Expression expr) {
            EmitQuote((UnaryExpression)expr);
        }

        //CONFORMING
        private void EmitQuote(UnaryExpression quote) {
            // emit the quoted expression as a runtime constant
            EmitConstant(quote.Operand, quote.Type);

            // Heuristic: only emit the tree rewrite logic if we have hoisted
            // locals.
            if (_scope.NearestHoistedLocals != null) {
                // HoistedLocals is internal so emit as System.Object
                EmitConstant(_scope.NearestHoistedLocals, typeof(object));
                _scope.EmitGet(_scope.NearestHoistedLocals.SelfVariable);
                _ilg.Emit(OpCodes.Call, typeof(RuntimeOps).GetMethod("Quote"));

                if (quote.Type != typeof(Expression)) {
                    _ilg.Emit(OpCodes.Castclass, quote.Type);
                }
            }
        }

        private void EmitThrowUnaryExpression(Expression expr) {
            EmitThrow((UnaryExpression)expr, EmitAs.Default);
        }

        private void EmitThrow(UnaryExpression expr, EmitAs emitAs) {
            if (expr.Operand == null) {
                CheckRethrow();

                _ilg.Emit(OpCodes.Rethrow);
            } else {
                EmitExpression(expr.Operand);
                _ilg.Emit(OpCodes.Throw);
            }
            if (emitAs != EmitAs.Void && expr.Type != typeof(void)) {
                _ilg.EmitDefault(expr.Type);
            }
        }

        private void EmitUnaryExpression(Expression expr) {
            EmitUnary((UnaryExpression)expr);
        }

        //CONFORMING
        private void EmitUnary(UnaryExpression node) {
            if (node.Method != null) {
                EmitUnaryMethod(node);
            } else if (node.NodeType == ExpressionType.NegateChecked && TypeUtils.IsInteger(node.Operand.Type)) {
                _ilg.EmitInt(0);
                _ilg.EmitConvertToType(typeof(int), node.Operand.Type, false);
                EmitExpression(node.Operand);
                EmitBinaryOperator(ExpressionType.SubtractChecked, node.Operand.Type, node.Operand.Type, node.Type, false);
            } else {
                EmitExpression(node.Operand);
                EmitUnaryOperator(node.NodeType, node.Operand.Type, node.Type);
            }
        }

        //CONFORMING
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private void EmitUnaryOperator(ExpressionType op, Type operandType, Type resultType) {
            bool operandIsNullable = TypeUtils.IsNullableType(operandType);

            if (op == ExpressionType.ArrayLength) {
                _ilg.Emit(OpCodes.Ldlen);
                return;
            }

            if (operandIsNullable) {
                switch (op) {
                    case ExpressionType.Not: {
                            if (operandType != typeof(bool?))
                                goto case ExpressionType.Negate;

                            Label labIfNull = _ilg.DefineLabel();
                            Label labEnd = _ilg.DefineLabel();
                            LocalBuilder loc = GetLocal(operandType);

                            // store values (reverse order since they are already on the stack)
                            _ilg.Emit(OpCodes.Stloc, loc);

                            // test for null
                            _ilg.Emit(OpCodes.Ldloca, loc);
                            _ilg.EmitHasValue(operandType);
                            _ilg.Emit(OpCodes.Brfalse_S, labEnd);

                            // do op on non-null value
                            _ilg.Emit(OpCodes.Ldloca, loc);
                            _ilg.EmitGetValueOrDefault(operandType);
                            Type nnOperandType = TypeUtils.GetNonNullableType(operandType);
                            EmitUnaryOperator(op, nnOperandType, typeof(bool));

                            // construct result
                            ConstructorInfo ci = resultType.GetConstructor(new Type[] { typeof(bool) });
                            _ilg.Emit(OpCodes.Newobj, ci);
                            _ilg.Emit(OpCodes.Stloc, loc);

                            _ilg.MarkLabel(labEnd);
                            _ilg.Emit(OpCodes.Ldloc, loc);
                            FreeLocal(loc);
                            return;
                        }
                    case ExpressionType.UnaryPlus:
                    case ExpressionType.NegateChecked:
                    case ExpressionType.Negate:
                    case ExpressionType.Increment:
                    case ExpressionType.Decrement:
                    case ExpressionType.OnesComplement:
                    case ExpressionType.IsFalse:
                    case ExpressionType.IsTrue: {
                            Debug.Assert(operandType == resultType);
                            Label labIfNull = _ilg.DefineLabel();
                            Label labEnd = _ilg.DefineLabel();
                            LocalBuilder loc = GetLocal(operandType);

                            // check for null
                            _ilg.Emit(OpCodes.Stloc, loc);
                            _ilg.Emit(OpCodes.Ldloca, loc);
                            _ilg.EmitHasValue(operandType);
                            _ilg.Emit(OpCodes.Brfalse_S, labIfNull);

                            // apply operator to non-null value
                            _ilg.Emit(OpCodes.Ldloca, loc);
                            _ilg.EmitGetValueOrDefault(operandType);
                            Type nnOperandType = TypeUtils.GetNonNullableType(resultType);
                            EmitUnaryOperator(op, nnOperandType, nnOperandType);

                            // construct result
                            ConstructorInfo ci = resultType.GetConstructor(new Type[] { nnOperandType });
                            _ilg.Emit(OpCodes.Newobj, ci);
                            _ilg.Emit(OpCodes.Stloc, loc);
                            _ilg.Emit(OpCodes.Br_S, labEnd);

                            // if null then create a default one
                            _ilg.MarkLabel(labIfNull);
                            _ilg.Emit(OpCodes.Ldloca, loc);
                            _ilg.Emit(OpCodes.Initobj, resultType);

                            _ilg.MarkLabel(labEnd);
                            _ilg.Emit(OpCodes.Ldloc, loc);
                            FreeLocal(loc);
                            return;
                        }
                    case ExpressionType.TypeAs:
                        _ilg.Emit(OpCodes.Box, operandType);
                        _ilg.Emit(OpCodes.Isinst, resultType);
                        if (TypeUtils.IsNullableType(resultType)) {
                            _ilg.Emit(OpCodes.Unbox_Any, resultType);
                        }
                        return;
                    default:
                        throw Error.UnhandledUnary(op);
                }
            } else {
                switch (op) {
                    case ExpressionType.Not:
                        if (operandType == typeof(bool)) {
                            _ilg.Emit(OpCodes.Ldc_I4_0);
                            _ilg.Emit(OpCodes.Ceq);
                        } else {
                            _ilg.Emit(OpCodes.Not);
                        }
                        break;
                    case ExpressionType.OnesComplement:
                        _ilg.Emit(OpCodes.Not);
                        break;
                    case ExpressionType.IsFalse:
                        _ilg.Emit(OpCodes.Ldc_I4_0);
                        _ilg.Emit(OpCodes.Ceq);
                        break;
                    case ExpressionType.IsTrue:
                        _ilg.Emit(OpCodes.Ldc_I4_1);
                        _ilg.Emit(OpCodes.Ceq);
                        break;
                    case ExpressionType.UnaryPlus:
                        _ilg.Emit(OpCodes.Nop);
                        break;
                    case ExpressionType.Negate:
                    case ExpressionType.NegateChecked:
                        _ilg.Emit(OpCodes.Neg);
                        break;
                    case ExpressionType.TypeAs:
                        if (operandType.IsValueType) {
                            _ilg.Emit(OpCodes.Box, operandType);
                        }
                        _ilg.Emit(OpCodes.Isinst, resultType);
                        if (TypeUtils.IsNullableType(resultType)) {
                            _ilg.Emit(OpCodes.Unbox_Any, resultType);
                        }
                        break;
                    case ExpressionType.Increment:
                        EmitConstantOne(resultType);
                        _ilg.Emit(OpCodes.Add);
                        break;
                    case ExpressionType.Decrement:
                        EmitConstantOne(resultType);
                        _ilg.Emit(OpCodes.Sub);
                        break;
                    default:
                        throw Error.UnhandledUnary(op);
                }

                EmitConvertArithmeticResult(op, resultType);
            }
        }

        private void EmitConstantOne(Type type) {
            switch (Type.GetTypeCode(type)) {
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.Int16:
                case TypeCode.Int32:
                    _ilg.Emit(OpCodes.Ldc_I4_1);
                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    _ilg.Emit(OpCodes.Ldc_I8, (long)1);
                    break;
                case TypeCode.Single:
                    _ilg.Emit(OpCodes.Ldc_R4, 1.0f);
                    break;
                case TypeCode.Double:
                    _ilg.Emit(OpCodes.Ldc_R8, 1.0d);
                    break;
                default:
                    // we only have to worry about aritmetic types, see
                    // TypeUtils.IsArithmetic
                    throw ContractUtils.Unreachable;
            }
        }

        private void EmitUnboxUnaryExpression(Expression expr) {
            var node = (UnaryExpression)expr;
            Debug.Assert(node.Type.IsValueType && !TypeUtils.IsNullableType(node.Type));

            // Unbox_Any leaves the value on the stack
            EmitExpression(node.Operand);
            _ilg.Emit(OpCodes.Unbox_Any, node.Type);
        }

        private void EmitConvertUnaryExpression(Expression expr) {
            EmitConvert((UnaryExpression)expr);
        }

        //CONFORMING
        private void EmitConvert(UnaryExpression node) {
            if (node.Method != null) {
                // User-defined conversions are only lifted if both source and
                // destination types are value types.  The C# compiler gets this wrong.
                // In C#, if you have an implicit conversion from int->MyClass and you
                // "lift" the conversion to int?->MyClass then a null int? goes to a
                // null MyClass.  This is contrary to the specification, which states
                // that the correct behaviour is to unwrap the int?, throw an exception
                // if it is null, and then call the conversion.
                //
                // We cannot fix this in C# but there is no reason why we need to
                // propagate this bug into the expression tree API.  Unfortunately
                // this means that when the C# compiler generates the lambda
                // (int? i)=>(MyClass)i, we will get different results for converting
                // that lambda to a delegate directly and converting that lambda to
                // an expression tree and then compiling it.  We can live with this
                // discrepancy however.

                if (node.IsLifted && (!node.Type.IsValueType || !node.Operand.Type.IsValueType)) {
                    ParameterInfo[] pis = node.Method.GetParametersCached();
                    Debug.Assert(pis != null && pis.Length == 1);
                    Type paramType = pis[0].ParameterType;
                    if (paramType.IsByRef) {
                        paramType = paramType.GetElementType();
                    }

                    UnaryExpression e = Expression.Convert(
                        Expression.Call(
                            node.Method,
                            Expression.Convert(node.Operand, pis[0].ParameterType)
                        ),
                        node.Type
                    );

                    EmitConvert(e);
                } else {
                    EmitUnaryMethod(node);
                }
            } else if (node.Type == typeof(void)) {
                EmitExpressionAsVoid(node.Operand);
            } else {
                EmitExpression(node.Operand);
                _ilg.EmitConvertToType(node.Operand.Type, node.Type, node.NodeType == ExpressionType.ConvertChecked);
            }
        }

        //CONFORMING
        private void EmitUnaryMethod(UnaryExpression node) {
            if (node.IsLifted) {
                ParameterExpression v = Expression.Variable(TypeUtils.GetNonNullableType(node.Operand.Type), null);
                MethodCallExpression mc = Expression.Call(node.Method, v);

                Type resultType = TypeUtils.GetNullableType(mc.Type);
                EmitLift(node.NodeType, resultType, mc, new ParameterExpression[] { v }, new Expression[] { node.Operand });
                _ilg.EmitConvertToType(resultType, node.Type, false);
            } else {
                EmitMethodCallExpression(Expression.Call(node.Method, node.Operand));
            }
        }
    }
}
