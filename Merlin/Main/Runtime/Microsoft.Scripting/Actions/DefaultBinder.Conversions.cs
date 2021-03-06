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
using System.Linq.Expressions;
using System.Reflection;
using System.Dynamic;
using Microsoft.Scripting.Generation;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

namespace Microsoft.Scripting.Actions {
    using Ast = System.Linq.Expressions.Expression;
    using AstUtils = Microsoft.Scripting.Ast.Utils;

    public partial class DefaultBinder : ActionBinder {
        public DynamicMetaObject ConvertTo(Type toType, ConversionResultKind kind, DynamicMetaObject arg) {
            ContractUtils.RequiresNotNull(toType, "toType");
            ContractUtils.RequiresNotNull(arg, "arg");

            Type knownType = arg.LimitType;

            // try all the conversions - first look for conversions against the expression type,
            // these can be done w/o any additional tests.  Then look for conversions against the 
            // restricted type.
            BindingRestrictions typeRestrictions = arg.Restrictions.Merge(BindingRestrictions.GetTypeRestriction(arg.Expression, arg.LimitType));

            return
                TryConvertToObject(toType, arg.Expression.Type, arg) ??
                TryAllConversions(toType, kind, arg.Expression.Type, arg.Restrictions, arg) ??
                TryAllConversions(toType, kind, arg.LimitType, typeRestrictions, arg) ??
                MakeErrorTarget(toType, kind, typeRestrictions, arg);
        }

        #region Conversion attempt helpers

        /// <summary>
        /// Checks if the conversion is to object and produces a target if it is.
        /// </summary>
        private static DynamicMetaObject TryConvertToObject(Type toType, Type knownType, DynamicMetaObject arg) {
            if (toType == typeof(object)) {
                if (knownType.IsValueType) {
                    return MakeBoxingTarget(arg);
                } else {
                    return arg;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if any conversions are available and if so builds the target for that conversion.
        /// </summary>
        private DynamicMetaObject TryAllConversions(Type toType, ConversionResultKind kind, Type knownType, BindingRestrictions restrictions, DynamicMetaObject arg) {
            return
                TryAssignableConversion(toType, knownType, restrictions, arg) ??           // known type -> known type
                TryExtensibleConversion(toType, knownType, restrictions, arg) ??           // Extensible<T> -> Extensible<T>.Value
                TryUserDefinedConversion(kind, toType, knownType, restrictions, arg) ??    // op_Implicit
                TryImplicitNumericConversion(toType, knownType, restrictions, arg) ??      // op_Implicit
                TryNullableConversion(toType, kind, knownType, restrictions, arg) ??       // null -> Nullable<T> or T -> Nullable<T>
                TryNullConversion(toType, knownType, restrictions);                        // null -> reference type
        }

        /// <summary>
        /// Checks if the conversion can be handled by a simple cast.
        /// </summary>
        private static DynamicMetaObject TryAssignableConversion(Type toType, Type type, BindingRestrictions restrictions, DynamicMetaObject arg) {
            if (toType.IsAssignableFrom(type) ||
                (type == typeof(DynamicNull) && (toType.IsClass || toType.IsInterface))) {
                // MakeSimpleConversionTarget handles the ConversionResultKind check
                return MakeSimpleConversionTarget(toType, restrictions, arg);
            }

            return null;
        }

        /// <summary>
        /// Checks if the conversion can be handled by calling a user-defined conversion method.
        /// </summary>
        private DynamicMetaObject TryUserDefinedConversion(ConversionResultKind kind, Type toType, Type type, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Type fromType = GetUnderlyingType(type);

            DynamicMetaObject res =
                   TryOneConversion(kind, toType, type, fromType, "op_Implicit", true, restrictions, arg) ??
                   TryOneConversion(kind, toType, type, fromType, "ConvertTo" + toType.Name, true, restrictions, arg);

            if (kind == ConversionResultKind.ExplicitCast ||
                kind == ConversionResultKind.ExplicitTry) {
                // finally try explicit conversions
                res = res ??
                    TryOneConversion(kind, toType, type, fromType, "op_Explicit", false, restrictions, arg) ??
                    TryOneConversion(kind, toType, type, fromType, "ConvertTo" + toType.Name, false, restrictions, arg);
            }

            return res;
        }

        /// <summary>
        /// Helper that checkes both types to see if either one defines the specified conversion
        /// method.
        /// </summary>
        private DynamicMetaObject TryOneConversion(ConversionResultKind kind, Type toType, Type type, Type fromType, string methodName, bool isImplicit, BindingRestrictions restrictions, DynamicMetaObject arg) {
            OldConvertToAction action = OldConvertToAction.Make(this, toType, kind);

            MemberGroup conversions = GetMember(action, fromType, methodName);
            DynamicMetaObject res = TryUserDefinedConversion(kind, toType, type, conversions, isImplicit, restrictions, arg);
            if (res != null) {
                return res;
            }

            // then on the type we're trying to convert to
            conversions = GetMember(action, toType, methodName);
            return TryUserDefinedConversion(kind, toType, type, conversions, isImplicit, restrictions, arg);
        }

        /// <summary>
        /// Checks if any of the members of the MemberGroup provide the applicable conversion and 
        /// if so uses it to build a conversion rule.
        /// </summary>
        private static DynamicMetaObject TryUserDefinedConversion(ConversionResultKind kind, Type toType, Type type, MemberGroup conversions, bool isImplicit, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Type checkType = GetUnderlyingType(type);

            foreach (MemberTracker mt in conversions) {
                if (mt.MemberType != TrackerTypes.Method) continue;

                MethodTracker method = (MethodTracker)mt;

                if (isImplicit && method.Method.IsDefined(typeof(ExplicitConversionMethodAttribute), true)) {
                    continue;
                }

                if (method.Method.ReturnType == toType) {   // TODO: IsAssignableFrom?  IsSubclass?
                    ParameterInfo[] pis = method.Method.GetParameters();

                    if (pis.Length == 1 && pis[0].ParameterType.IsAssignableFrom(checkType)) {
                        // we can use this method
                        if (type == checkType) {
                            return MakeConversionTarget(kind, method, type, isImplicit, restrictions, arg);
                        } else {
                            return MakeExtensibleConversionTarget(kind, method, type, isImplicit, restrictions, arg);
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if the conversion is to applicable by extracting the value from Extensible of T.
        /// </summary>
        private static DynamicMetaObject TryExtensibleConversion(Type toType, Type type, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Type extensibleType = typeof(Extensible<>).MakeGenericType(toType);
            if (extensibleType.IsAssignableFrom(type)) {
                return MakeExtensibleTarget(extensibleType, restrictions, arg);
            }
            return null;
        }

        /// <summary>
        /// Checks if there's an implicit numeric conversion for primitive data types.
        /// </summary>
        private static DynamicMetaObject TryImplicitNumericConversion(Type toType, Type type, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Type checkType = type;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Extensible<>)) {
                checkType = type.GetGenericArguments()[0];
            }

            if (TypeUtils.IsNumeric(toType) && TypeUtils.IsNumeric(checkType)) {
                // check for an explicit conversion
                int toX, toY, fromX, fromY;
                if (TypeUtils.GetNumericConversionOrder(Type.GetTypeCode(toType), out toX, out toY) &&
                    TypeUtils.GetNumericConversionOrder(Type.GetTypeCode(checkType), out fromX, out fromY)) {
                    if (TypeUtils.IsImplicitlyConvertible(fromX, fromY, toX, toY)) {
                        // MakeSimpleConversionTarget handles the ConversionResultKind check
                        if (type == checkType) {
                            return MakeSimpleConversionTarget(toType, restrictions, arg);
                        } else {
                            return MakeSimpleExtensibleConversionTarget(toType, restrictions, arg);
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if there's a conversion to/from Nullable of T.
        /// </summary>
        private DynamicMetaObject TryNullableConversion(Type toType, ConversionResultKind kind, Type knownType, BindingRestrictions restrictions, DynamicMetaObject arg) {
            if (toType.IsGenericType && toType.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                if (knownType == typeof(DynamicNull)) {
                    // null -> Nullable<T>
                    return MakeNullToNullableOfTTarget(toType, restrictions);
                } else if (knownType == toType.GetGenericArguments()[0]) {
                    return MakeTToNullableOfTTarget(toType, knownType, restrictions, arg);
                } else if (kind == ConversionResultKind.ExplicitCast || kind == ConversionResultKind.ExplicitTry) {
                    if (knownType != typeof(object)) {
                        // when doing an explicit cast we'll do things like int -> Nullable<float>
                        return MakeConvertingToTToNullableOfTTarget(toType, kind, restrictions, arg);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks to see if there's a conversion of null to a reference type
        /// </summary>
        private static DynamicMetaObject TryNullConversion(Type toType, Type knownType, BindingRestrictions restrictions) {
            if (knownType == typeof(DynamicNull) && !toType.IsValueType) {
                return MakeNullTarget(toType, restrictions);
            }
            return null;
        }

        #endregion

        #region Rule production helpers

        /// <summary>
        /// Helper to produce an error when a conversion cannot occur
        /// </summary>
        private DynamicMetaObject MakeErrorTarget(Type toType, ConversionResultKind kind, BindingRestrictions restrictions, DynamicMetaObject arg) {
            DynamicMetaObject target;

            switch (kind) {
                case ConversionResultKind.ImplicitCast:
                case ConversionResultKind.ExplicitCast:
                    target = MakeError(
                        MakeConversionError(toType, arg.Expression),
                        restrictions
                    );
                    break;
                case ConversionResultKind.ImplicitTry:
                case ConversionResultKind.ExplicitTry:
                    target = new DynamicMetaObject(
                        GetTryConvertReturnValue(toType),
                        restrictions
                    );
                    break;
                default:
                    throw new InvalidOperationException(kind.ToString());
            }

            return target;
        }

        /// <summary>
        /// Helper to produce a rule which just boxes a value type
        /// </summary>
        private static DynamicMetaObject MakeBoxingTarget(DynamicMetaObject arg) {
            // MakeSimpleConversionTarget handles the ConversionResultKind check
            return MakeSimpleConversionTarget(typeof(object), arg.Restrictions, arg);
        }

        /// <summary>
        /// Helper to produce a conversion rule by calling the helper method to do the convert
        /// </summary>
        private static DynamicMetaObject MakeConversionTarget(ConversionResultKind kind, MethodTracker method, Type fromType, bool isImplicit, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Expression param = AstUtils.Convert(arg.Expression, fromType);

            return MakeConversionTargetWorker(kind, method, isImplicit, restrictions, param);
        }

        /// <summary>
        /// Helper to produce a conversion rule by calling the helper method to do the convert
        /// </summary>
        private static DynamicMetaObject MakeExtensibleConversionTarget(ConversionResultKind kind, MethodTracker method, Type fromType, bool isImplicit, BindingRestrictions restrictions, DynamicMetaObject arg) {
            return MakeConversionTargetWorker(kind, method, isImplicit, restrictions, GetExtensibleValue(fromType, arg));
        }

        /// <summary>
        /// Helper to produce a conversion rule by calling the method to do the convert.  This version takes the parameter
        /// to be passed to the conversion function and we call it w/ our own value or w/ our Extensible.Value.
        /// </summary>
        private static DynamicMetaObject MakeConversionTargetWorker(ConversionResultKind kind, MethodTracker method, bool isImplicit, BindingRestrictions restrictions, Expression param) {
            return new DynamicMetaObject(
                WrapForThrowingTry(
                    kind,
                    isImplicit,
                    AstUtils.SimpleCallHelper(
                        method.Method,
                        param
                    ),
                    method.Method.ReturnType
                ),
                restrictions
            );
        }

        /// <summary>
        /// Helper to wrap explicit conversion call into try/catch incase it throws an exception.  If
        /// it throws the default value is returned.
        /// </summary>
        private static Expression WrapForThrowingTry(ConversionResultKind kind, bool isImplicit, Expression ret, Type retType) {
            if (!isImplicit && kind == ConversionResultKind.ExplicitTry) {
                Expression convFailed = GetTryConvertReturnValue(retType);
                ParameterExpression tmp = Ast.Variable(convFailed.Type == typeof(object) ? typeof(object) : ret.Type, "tmp");
                ret = Ast.Block(
                        new ParameterExpression[] { tmp },
                        AstUtils.Try(
                            Ast.Assign(tmp, AstUtils.Convert(ret, tmp.Type))
                        ).Catch(
                            typeof(Exception),
                            Ast.Assign(tmp, convFailed)
                        ),
                        tmp
                     );
            }
            return ret;
        }

        /// <summary>
        /// Helper to produce a rule when no conversion is required (the strong type of the expression
        /// input matches the type we're converting to or has an implicit conversion at the IL level)
        /// </summary>
        private static DynamicMetaObject MakeSimpleConversionTarget(Type toType, BindingRestrictions restrictions, DynamicMetaObject arg) {
            return new DynamicMetaObject(
                AstUtils.Convert(arg.Expression, CompilerHelpers.GetVisibleType(toType)),
                restrictions);

            /*
            if (toType.IsValueType && _rule.ReturnType == typeof(object) && Expression.Type == typeof(object)) {
                // boxed value type is being converted back to object.  We've done 
                // the type check, there's no need to unbox & rebox the value.  infact 
                // it breaks calls on instance methods so we need to avoid it.
                _rule.Target =
                    _rule.MakeReturn(
                        Binder,
                        Expression
                    );
            } 
             * */
        }

        /// <summary>
        /// Helper to produce a rule when no conversion is required from an extensible type's
        /// underlying storage to the type we're converting to.  The type of extensible type
        /// matches the type we're converting to or has an implicit conversion at the IL level.
        /// </summary>
        private static DynamicMetaObject MakeSimpleExtensibleConversionTarget(Type toType, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Type extType = typeof(Extensible<>).MakeGenericType(toType);

            return new DynamicMetaObject(
                AstUtils.Convert(
                    GetExtensibleValue(extType, arg),
                    toType
                ),
                restrictions
            );
        }

        /// <summary>
        /// Helper to extract the value from an Extensible of T
        /// </summary>
        private static DynamicMetaObject MakeExtensibleTarget(Type extensibleType, BindingRestrictions restrictions, DynamicMetaObject arg) {
            return new DynamicMetaObject(
                Ast.Property(Ast.Convert(arg.Expression, extensibleType), extensibleType.GetProperty("Value")),
                restrictions
            );
        }

        /// <summary>
        /// Helper to convert a null value to nullable of T
        /// </summary>
        private static DynamicMetaObject MakeNullToNullableOfTTarget(Type toType, BindingRestrictions restrictions) {
            return new DynamicMetaObject(
                Ast.Call(typeof(ScriptingRuntimeHelpers).GetMethod("CreateInstance").MakeGenericMethod(toType)),
                restrictions
            );
        }

        /// <summary>
        /// Helper to produce the rule for converting T to Nullable of T
        /// </summary>
        private static DynamicMetaObject MakeTToNullableOfTTarget(Type toType, Type knownType, BindingRestrictions restrictions, DynamicMetaObject arg) {
            // T -> Nullable<T>
            return new DynamicMetaObject(
                Ast.New(
                    toType.GetConstructor(new Type[] { knownType }),
                    AstUtils.Convert(arg.Expression, knownType)
                ),
                restrictions
            );
        }

        /// <summary>
        /// Helper to produce the rule for converting T to Nullable of T
        /// </summary>
        private DynamicMetaObject MakeConvertingToTToNullableOfTTarget(Type toType, ConversionResultKind kind, BindingRestrictions restrictions, DynamicMetaObject arg) {
            Type valueType = toType.GetGenericArguments()[0];

            // ConvertSelfToT -> Nullable<T>
            if (kind == ConversionResultKind.ExplicitCast) {
                // if the conversion to T fails we just throw
                Expression conversion = ConvertExpression(arg.Expression, valueType, kind, Ast.Constant(null, typeof(CodeContext)));

                return new DynamicMetaObject(
                    Ast.New(
                        toType.GetConstructor(new Type[] { valueType }),
                        conversion
                    ),
                    restrictions
                );
            } else {
                Expression conversion = ConvertExpression(arg.Expression, valueType, kind, Ast.Constant(null, typeof(CodeContext)));

                // if the conversion to T succeeds then produce the nullable<T>, otherwise return default(retType)
                ParameterExpression tmp = Ast.Variable(typeof(object), "tmp");
                return new DynamicMetaObject(
                    Ast.Block(
                        new ParameterExpression[] { tmp },
                        Ast.Condition(
                            Ast.NotEqual(
                                Ast.Assign(tmp, conversion),
                                Ast.Constant(null)
                            ),
                            Ast.New(
                                toType.GetConstructor(new Type[] { valueType }),
                                Ast.Convert(
                                    tmp,
                                    valueType
                                )
                            ),
                            GetTryConvertReturnValue(toType)
                        )
                    ),
                    restrictions
                );
            }
        }

        /// <summary>
        /// Returns a value which indicates failure when a OldConvertToAction of ImplicitTry or
        /// ExplicitTry.
        /// </summary>
        public static Expression GetTryConvertReturnValue(Type type) {
            Expression res;
            if (type.IsInterface || type.IsClass) {
                res = Ast.Constant(null, type);
            } else {
                res = Ast.Constant(null);
            }

            return res;
        }

        /// <summary>
        /// Helper to extract the Value of an Extensible of T from the
        /// expression being converted.
        /// </summary>
        private static Expression GetExtensibleValue(Type extType, DynamicMetaObject arg) {
            return Ast.Property(
                AstUtils.Convert(
                    arg.Expression,
                    extType
                ),
                extType.GetProperty("Value")
            );
        }

        /// <summary>
        /// Helper that checks if fromType is an Extensible of T or a subtype of 
        /// Extensible of T and if so returns the T.  Otherwise it returns fromType.
        /// 
        /// This is used to treat extensible types the same as their underlying types.
        /// </summary>
        private static Type GetUnderlyingType(Type fromType) {
            Type curType = fromType;
            do {
                if (curType.IsGenericType && curType.GetGenericTypeDefinition() == typeof(Extensible<>)) {
                    fromType = curType.GetGenericArguments()[0];
                }
                curType = curType.BaseType;
            } while (curType != null);
            return fromType;
        }

        /// <summary>
        /// Creates a target which returns null for a reference type.
        /// </summary>
        private static DynamicMetaObject MakeNullTarget(Type toType, BindingRestrictions restrictions) {
            return new DynamicMetaObject(
                Ast.Convert(Ast.Constant(null), toType),
                restrictions
            );
        }

        #endregion
    }
}
