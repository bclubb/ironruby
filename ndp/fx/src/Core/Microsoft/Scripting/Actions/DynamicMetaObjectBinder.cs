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

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Dynamic.Utils;
using System.Linq.Expressions;
using System.Linq.Expressions.Compiler;
using System.Runtime.CompilerServices;

namespace System.Dynamic {
    /// <summary>
    /// The dynamic call site binder that participates in the <see cref="DynamicMetaObject"/> binding protocol.
    /// </summary>
    /// <remarks>
    /// The <see cref="CallSiteBinder"/> performs the binding of the dynamic operation using the runtime values
    /// as input. On the other hand, the <see cref="DynamicMetaObjectBinder"/> participates in the <see cref="DynamicMetaObject"/>
    /// binding protocol.
    /// </remarks>
    public abstract class DynamicMetaObjectBinder : CallSiteBinder {

        #region Standard Binder Kinds

        internal const int OperationBinderHash = 0x4000000;
        internal const int UnaryOperationBinderHash = 0x8000000;
        internal const int BinaryOperationBinderHash = 0xc000000;
        internal const int GetMemberBinderHash = 0x10000000;
        internal const int SetMemberBinderHash = 0x14000000;
        internal const int DeleteMemberBinderHash = 0x18000000;
        internal const int GetIndexBinderHash = 0x1c000000;
        internal const int SetIndexBinderHash = 0x20000000;
        internal const int DeleteIndexBinderHash = 0x24000000;
        internal const int InvokeMemberBinderHash = 0x28000000;
        internal const int ConvertBinderHash = 0x2c000000;
        internal const int CreateInstanceBinderHash = 0x30000000;
        internal const int InvokeBinderHash = 0x34000000;
        internal const int BinaryOperationOnMemberBinderHash = 0x38000000;
        internal const int BinaryOperationOnIndexBinderHash = 0x3c000000;
        internal const int UnaryOperationOnMemberBinderHash = 0x40000000;
        internal const int UnaryOperationOnIndexBinderHash = 0x44000000;

        #endregion

        #region Public APIs

        /// <summary>
        /// Initializes a new instance of the <see cref="DynamicMetaObjectBinder"/> class.
        /// </summary>
        protected DynamicMetaObjectBinder() {
        }

        /// <summary>
        /// Performs the runtime binding of the dynamic operation on a set of arguments.
        /// </summary>
        /// <param name="args">An array of arguments to the dynamic operation.</param>
        /// <param name="parameters">The array of <see cref="ParameterExpression"/> instances that represent the parameters of the call site in the binding process.</param>
        /// <param name="returnLabel">A LabelTarget used to return the result of the dynamic binding.</param>
        /// <returns>
        /// An Expression that performs tests on the dynamic operation arguments, and
        /// performs the dynamic operation if hte tests are valid. If the tests fail on
        /// subsequent occurrences of the dynamic operation, Bind will be called again
        /// to produce a new <see cref="Expression"/> for the new argument types.
        /// </returns>
        public sealed override Expression Bind(object[] args, ReadOnlyCollection<ParameterExpression> parameters, LabelTarget returnLabel) {
            if (args.Length == 0) {
                throw new InvalidOperationException();
            }

            DynamicMetaObject[] mos;
            if (args.Length != 1) {
                mos = new DynamicMetaObject[args.Length - 1];
                for (int i = 1; i < args.Length; i++) {
                    mos[i - 1] = ObjectToMetaObject(args[i], parameters[i]);
                }
            } else {
                mos = DynamicMetaObject.EmptyMetaObjects;
            }

            DynamicMetaObject binding = Bind(
                ObjectToMetaObject(args[0], parameters[0]),
                mos
            );

            if (binding == null) {
                throw Error.BindingCannotBeNull();
            }

            return GetMetaObjectRule(binding, returnLabel);
        }

        private static DynamicMetaObject ObjectToMetaObject(object argValue, Expression parameterExpression) {
            IDynamicObject ido = argValue as IDynamicObject;
            if (ido != null) {
                return ido.GetMetaObject(parameterExpression);
            } else {
                return new DynamicMetaObject(parameterExpression, BindingRestrictions.Empty, argValue);
            }
        }

        /// <summary>
        /// When overridden in the derived class, performs the binding of the dynamic operation.
        /// </summary>
        /// <param name="target">The target of the dynamic operation.</param>
        /// <param name="args">An array of arguments of the dynamic operation.</param>
        /// <returns>The <see cref="DynamicMetaObject"/> representing the result of the binding.</returns>
        public abstract DynamicMetaObject Bind(DynamicMetaObject target, DynamicMetaObject[] args);

        /// <summary>
        /// Defers the binding of the operation until later time when the runtime values of all dynamic operation arguments have been computed.
        /// </summary>
        /// <param name="target">The target of the dynamic operation.</param>
        /// <param name="args">An array of arguments of the dynamic operation.</param>
        /// <returns>The <see cref="DynamicMetaObject"/> representing the result of the binding.</returns>
        public DynamicMetaObject Defer(DynamicMetaObject target, params DynamicMetaObject[] args) {
            ContractUtils.RequiresNotNull(target, "target");

            if (args == null) {
                return MakeDeferred(
                        target.Restrictions,
                        target
                );
            } else {
                return MakeDeferred(
                        target.Restrictions.Merge(BindingRestrictions.Combine(args)),
                        args.AddFirst(target)
                );
            }
        }

        /// <summary>
        /// Defers the binding of the operation until later time when the runtime values of all dynamic operation arguments have been computed.
        /// </summary>
        /// <param name="args">An array of arguments of the dynamic operation.</param>
        /// <returns>The <see cref="DynamicMetaObject"/> representing the result of the binding.</returns>
        public DynamicMetaObject Defer(params DynamicMetaObject[] args) {
            return MakeDeferred(
                BindingRestrictions.Combine(args),
                args
            );
        }

        private DynamicMetaObject MakeDeferred(BindingRestrictions rs, params DynamicMetaObject[] args) {
            var exprs = DynamicMetaObject.GetExpressions(args);

            Type delegateType = DelegateHelpers.MakeDeferredSiteDelegate(args, typeof(object));

            // Because we know the arguments match the delegate type (we just created the argument types)
            // we go directly to DynamicExpression.Make to avoid a bunch of unnecessary argument validation
            return new DynamicMetaObject(
                DynamicExpression.Make(
                    typeof(object),
                    delegateType,
                    this,
                    new ReadOnlyCollection<Expression>(exprs)
                ),
                rs
            );
        }

        #endregion

        private Expression AddReturn(Expression body, LabelTarget @return) {
            switch (body.NodeType) {
                case ExpressionType.Conditional:
                    ConditionalExpression conditional = (ConditionalExpression)body;
                    if (IsDeferExpression(conditional.IfTrue)) {
                        return Expression.Condition(
                            Expression.Not(conditional.Test),
                            Expression.Return(@return, Helpers.Convert(conditional.IfFalse, @return.Type)),
                            Expression.Empty()
                        );
                    } else if (IsDeferExpression(conditional.IfFalse)) {
                        return Expression.Condition(
                            conditional.Test,
                            Expression.Return(@return, Helpers.Convert(conditional.IfTrue, @return.Type)),
                            Expression.Empty()
                        );
                    }
                    return Expression.Condition(
                        conditional.Test,
                        AddReturn(conditional.IfTrue, @return),
                        AddReturn(conditional.IfFalse, @return)
                    );
                case ExpressionType.Throw:
                    return body;
                case ExpressionType.Block:
                    // block could have a throw which we need to run through to avoid 
                    // trying to convert it
                    BlockExpression block = (BlockExpression)body;

                    int count = block.ExpressionCount;
                    Expression[] nodes = new Expression[count];

                    for (int i = 0; i < nodes.Length - 1; i++) {
                        nodes[i] = block.GetExpression(i);
                    }
                    nodes[nodes.Length - 1] = AddReturn(block.GetExpression(count - 1), @return);

                    return Expression.Block(block.Variables, nodes);
                default:
                    return Expression.Return(@return, Helpers.Convert(body, @return.Type));
            }
        }

        private bool IsDeferExpression(Expression e) {
            if (e.NodeType == ExpressionType.Dynamic) {
                return ((DynamicExpression)e).Binder == this;
            }

            if (e.NodeType == ExpressionType.Convert) {
                return IsDeferExpression(((UnaryExpression)e).Operand);
            }

            return false;
        }

        private Expression GetMetaObjectRule(DynamicMetaObject binding, LabelTarget @return) {
            Debug.Assert(binding != null);

            Expression body = AddReturn(binding.Expression, @return);

            if (binding.Restrictions != BindingRestrictions.Empty) {
                // add the test only if we have one
                body = Expression.Condition(
                    binding.Restrictions.ToExpression(),
                    body,
                    Expression.Empty()
                );
            }

            return body;
        }
    }
}
