﻿/* ****************************************************************************
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

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Scripting.Runtime;
using System.Text.RegularExpressions;
using IronRuby.Runtime;
using Microsoft.Scripting.Math;
using IronRuby.Builtins;
using Microsoft.Scripting;
using Microsoft.Scripting.Utils;

namespace IronRuby.StandardLibrary.BigDecimal {
    [RubyClass("BigDecimal", Inherits = typeof(Numeric), Extends = typeof(BigDecimal))]
    public sealed class BigDecimalOps {

        internal static readonly object BigDecimalOpsClassKey = new object();

        #region Static Fields

        internal static BigDecimal.Config GetConfig(RubyContext/*!*/ context) {
            ContractUtils.RequiresNotNull(context, "context");
            return (BigDecimal.Config)context.GetOrCreateLibraryData(BigDecimalOpsClassKey, () => new BigDecimal.Config());
        }

        #endregion

        #region Construction

        [RubyConstructor]
        public static BigDecimal/*!*/ CreateBigDecimal(RubyContext/*!*/ context, RubyClass/*!*/ self, [DefaultProtocol]MutableString/*!*/ value, [Optional]int n) {
            return BigDecimal.Create(GetConfig(context), value.ConvertToString(), n);
        }

        #endregion

        #region Constants

        [RubyConstant]
        public const uint BASE = BigDecimal.BASE;
        [RubyConstant]
        public const int EXCEPTION_ALL = (int)BigDecimal.OverflowExceptionModes.All;
        [RubyConstant]
        public const int EXCEPTION_INFINITY = (int)BigDecimal.OverflowExceptionModes.Infinity;
        [RubyConstant]
        public const int EXCEPTION_NaN = (int)BigDecimal.OverflowExceptionModes.NaN;
        [RubyConstant]
        public const int EXCEPTION_OVERFLOW = (int)BigDecimal.OverflowExceptionModes.Overflow;
        [RubyConstant]
        public const int EXCEPTION_UNDERFLOW = (int)BigDecimal.OverflowExceptionModes.Underflow;
        [RubyConstant]
        public const int EXCEPTION_ZERODIVIDE = (int)BigDecimal.OverflowExceptionModes.ZeroDivide;
        [RubyConstant]
        public const int ROUND_CEILING = (int)BigDecimal.RoundingModes.Ceiling;
        [RubyConstant]
        public const int ROUND_DOWN = (int)BigDecimal.RoundingModes.Down;
        [RubyConstant]
        public const int ROUND_FLOOR = (int)BigDecimal.RoundingModes.Floor;
        [RubyConstant]
        public const int ROUND_HALF_DOWN = (int)BigDecimal.RoundingModes.HalfDown;
        [RubyConstant]
        public const int ROUND_HALF_EVEN = (int)BigDecimal.RoundingModes.HalfEven;
        [RubyConstant]
        public const int ROUND_HALF_UP = (int)BigDecimal.RoundingModes.HalfUp;
        [RubyConstant]
        public const int ROUND_UP = (int)BigDecimal.RoundingModes.Up;
        [RubyConstant]
        public const int ROUND_MODE = 256;
        [RubyConstant]
        public const int SIGN_NEGATIVE_FINITE = -2;
        [RubyConstant]
        public const int SIGN_NEGATIVE_INFINITE = -3;
        [RubyConstant]
        public const int SIGN_NEGATIVE_ZERO = -1;
        [RubyConstant]
        public const int SIGN_NaN = 0;
        [RubyConstant]
        public const int SIGN_POSITIVE_FINITE = 2;
        [RubyConstant]
        public const int SIGN_POSITIVE_INFINITE = 3;
        [RubyConstant]
        public const int SIGN_POSITIVE_ZERO = 1;
        #endregion

        #region Singleton Methods

        [RubyMethod("_load", RubyMethodAttributes.PublicSingleton)]
        public static BigDecimal/*!*/ Load(RubyContext/*!*/ context, RubyClass/*!*/ self, [DefaultProtocol]MutableString/*!*/ str) {
            try {
                MutableString[] components = str.Split(new char[] { ':' }, 2, StringSplitOptions.None);
                int maxDigits = 0;
                int maxPrecision = 1;
                string digits = "";

                if (!(components[0] == null) || components[0].IsEmpty) {
                    maxDigits = int.Parse(components[0].ToString());
                }
                if (maxDigits != 0) {
                    maxPrecision = maxDigits / BigDecimal.BASE_FIG + (maxDigits % BigDecimal.BASE_FIG == 0 ? 0 : 1);
                }
                if (components.Length == 2 && components[1] != null) {
                    digits = components[1].ToString();
                }

                return BigDecimal.Create(GetConfig(context), digits, maxPrecision);
            } catch {
                throw RubyExceptions.CreateTypeError("load failed: invalid character in the marshaled string.");
            }
        }

        [RubyMethod("double_fig", RubyMethodAttributes.PublicSingleton)]
        public static int DoubleFig(RubyClass/*!*/ self) {
            return 16; // This is the number of digits (in the mantissa?) that a Double can hold.
        }

        [RubyMethod("mode", RubyMethodAttributes.PublicSingleton)]
        public static int Mode(RubyContext/*!*/ context, RubyClass/*!*/ self, int mode) {
            if (mode == ROUND_MODE) {
                return (int)GetConfig(context).RoundingMode;
            } else {
                return (int)GetConfig(context).OverflowMode & mode;
            }
        }

        [RubyMethod("mode", RubyMethodAttributes.PublicSingleton)]
        public static int Mode(RubyContext/*!*/ context, RubyClass/*!*/ self, int mode, object value) {
            if (value == null) {
                return Mode(context, self, mode);
            }
            if (mode == ROUND_MODE) {
                if (value is int) {
                    GetConfig(context).RoundingMode = (BigDecimal.RoundingModes)value;
                    return (int)value;
                } else {
                    throw RubyExceptions.CreateTypeError("wrong argument type " + value.ToString() + " (expected Fixnum)");
                }
            } else {
                if (value is bool) {
                    BigDecimal.Config config = GetConfig(context);
                    if (Enum.IsDefined(typeof(BigDecimal.OverflowExceptionModes), mode)) {
                        BigDecimal.OverflowExceptionModes enumMode = (BigDecimal.OverflowExceptionModes)mode;
                        if ((bool)value) {
                            config.OverflowMode = config.OverflowMode | enumMode;
                        } else {
                            config.OverflowMode = config.OverflowMode & (BigDecimal.OverflowExceptionModes.All ^ enumMode);
                        }
                    }
                    return (int)config.OverflowMode;
                } else {
                    throw RubyExceptions.CreateTypeError("second argument must be true or false");
                }
            }
        }

        [RubyMethod("limit", RubyMethodAttributes.PublicSingleton)]
        public static int Limit(RubyContext/*!*/ context, RubyClass/*!*/ self, int n) {
            BigDecimal.Config config = GetConfig(context);
            int limit = config.Limit;
            config.Limit = n;
            return limit;
        }

        [RubyMethod("limit", RubyMethodAttributes.PublicSingleton)]
        public static int Limit(RubyContext/*!*/ context, RubyClass/*!*/ self, [Optional]object n) {
            if (!(n is Missing)) {
                throw RubyExceptions.CreateTypeError("wrong argument type " + RubyUtils.GetClassName(self.Context, n) + " (expected Fixnum)");
            }
            return GetConfig(context).Limit;
        }

        [RubyMethod("ver", RubyMethodAttributes.PublicSingleton)]
        public static MutableString/*!*/ Version(RubyClass/*!*/ self) {
            return MutableString.Create("1.0.1");
        }

        #region induced_from

        [RubyMethod("induced_from", RubyMethodAttributes.PublicSingleton)]
        public static BigDecimal InducedFrom(RubyClass/*!*/ self, [NotNull]BigDecimal/*!*/ value) {
            return value;
        }

        [RubyMethod("induced_from", RubyMethodAttributes.PublicSingleton)]
        public static BigDecimal InducedFrom(RubyContext/*!*/ context, RubyClass/*!*/ self, int value) {
            return BigDecimal.Create(GetConfig(context), value.ToString());
        }

        [RubyMethod("induced_from", RubyMethodAttributes.PublicSingleton)]
        public static BigDecimal InducedFrom(RubyContext/*!*/ context, RubyClass/*!*/ self, [NotNull]BigInteger/*!*/ value) {
            return BigDecimal.Create(GetConfig(context), value.ToString());
        }

        [RubyMethod("induced_from", RubyMethodAttributes.PublicSingleton)]
        public static BigDecimal InducedFrom(RubyClass/*!*/ self, object value) {
            throw RubyExceptions.CreateTypeConversionError(RubyUtils.GetClassName(self.Context, value), self.Name);
        }

        #endregion

        #endregion

        #region Instance Methods

        #region Properties

        [RubyMethod("sign")]
        public static int Sign(BigDecimal/*!*/ self) {
            return self.GetSignCode();
        }

        [RubyMethod("exponent")]
        public static int Exponent(BigDecimal/*!*/ self) {
            return self.Exponent;
        }

        [RubyMethod("precs")]
        public static RubyArray/*!*/ Precision(BigDecimal/*!*/ self) {
            return RubyOps.MakeArray2(self.Precision * BigDecimal.BASE_FIG, self.MaxPrecision * BigDecimal.BASE_FIG);
        }

        [RubyMethod("split")]
        public static RubyArray/*!*/ Split(BigDecimal/*!*/ self) {
            return RubyOps.MakeArray4(self.Sign, MutableString.Create(self.GetFractionString()), 10, self.Exponent);
        }

        [RubyMethod("fix")]
        public static BigDecimal/*!*/ Fix(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            return BigDecimal.IntegerPart(GetConfig(context), self);
        }

        [RubyMethod("frac")]
        public static BigDecimal/*!*/ Fraction(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            return BigDecimal.FractionalPart(GetConfig(context), self);
        }

        #endregion

        #region Conversion Operations

        #region to_s

        [RubyMethod("to_s")]
        public static MutableString/*!*/ ToString(BigDecimal/*!*/ self) {
            return MutableString.Create(self.ToString());
        }

        [RubyMethod("to_s")]
        public static MutableString/*!*/ ToString(BigDecimal/*!*/ self, [DefaultProtocol]int separateAt) {
            return MutableString.Create(self.ToString(separateAt));
        }

        [RubyMethod("to_s")]
        public static MutableString/*!*/ ToString(BigDecimal/*!*/ self, [DefaultProtocol][NotNull]MutableString/*!*/ format) {
            string posSign = "";
            int separateAt = 0;

            Match m = Regex.Match(format.ConvertToString(), @"^(?<posSign>[+ ])?(?<separateAt>\d+)?(?<floatFormat>[fF])?", RegexOptions.ExplicitCapture);
            Group posSignGroup = m.Groups["posSign"];
            Group separateAtGroup = m.Groups["separateAt"];
            Group floatFormatGroup = m.Groups["floatFormat"];

            if (posSignGroup.Success) {
                posSign = m.Groups["posSign"].Value;
            }
            if (separateAtGroup.Success) {
                separateAt = Int32.Parse(m.Groups["separateAt"].Value);
            }
            bool floatFormat = floatFormatGroup.Success;
            return MutableString.Create(self.ToString(separateAt, posSign, floatFormat));
        }

        #endregion

        [RubyMethod("inspect")]
        public static MutableString/*!*/ Inspect(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            MutableString str = MutableString.CreateMutable();
            str.AppendFormat("#<{0}:", context.GetClassOf(self).Name);
            RubyUtils.AppendFormatHexObjectId(str, RubyUtils.GetObjectId(context, self));
            str.AppendFormat(",'{0}',", self.ToString(10));
            str.AppendFormat("{0}({1})>", self.PrecisionDigits.ToString(), self.MaxPrecisionDigits.ToString());
            return str;
        }

        #region coerce

        [RubyMethod("coerce")]
        public static RubyArray/*!*/ Coerce(BigDecimal/*!*/ self, BigDecimal/*!*/ other) {
            return RubyOps.MakeArray2(other, self);
        }

        [RubyMethod("coerce")]
        public static RubyArray/*!*/ Coerce(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            return RubyOps.MakeArray2(other, ToFloat(context, self));
        }

        [RubyMethod("coerce")]
        public static RubyArray/*!*/ Coerce(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            return RubyOps.MakeArray2(BigDecimal.Create(GetConfig(context), other.ToString()), self);
        }

        [RubyMethod("coerce")]
        public static RubyArray/*!*/ Coerce(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigInteger/*!*/ other) {
            return RubyOps.MakeArray2(BigDecimal.Create(GetConfig(context), other.ToString()), self);
        }

        #endregion

        #region _dump

        [RubyMethod("_dump")]
        public static MutableString/*!*/ Dump(BigDecimal/*!*/ self, [Optional]object limit) {
            // We ignore the limit value as BigDecimal does not contain other objects.
            return MutableString.CreateMutable(
                string.Format("{0}:{1}",
                    self.MaxPrecisionDigits,
                    self));
        }

        #endregion

        [RubyMethod("to_f")]
        public static double ToFloat(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            return BigDecimal.ToFloat(GetConfig(context), self);
        }

        [RubyMethod("to_i")]
        [RubyMethod("to_int")]
        public static object ToI(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            return BigDecimal.ToInteger(GetConfig(context), self);
        }

        [RubyMethod("hash")]
        public static int Hash(BigDecimal/*!*/ self) {
            return self.GetHashCode();
        }

        #endregion

        #region Arithmetic Operations

        #region add, +

        [RubyMethod("+")]
        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            return BigDecimal.Add(GetConfig(context), self, other);
        }

        [RubyMethod("+")]
        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Add(config, self, BigDecimal.Create(config, other));
        }

        [RubyMethod("+")]
        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Add(config, self, BigDecimal.Create(config, other));
        }

        [RubyMethod("+")]
        [RubyMethod("add")]
        public static object Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Add);
        }

        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other, int n) {
            return BigDecimal.Add(GetConfig(context), self, other, n);
        }

        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Add(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Add(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("add")]
        public static BigDecimal/*!*/ Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Add(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("add")]
        public static object Add(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other, [DefaultProtocol]int n) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Add);
        }

        #endregion

        #region sub, -

        [RubyMethod("-")]
        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other) {
            return BigDecimal.Subtract(GetConfig(context), self, other);
        }

        [RubyMethod("-")]
        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Subtract(config, self, BigDecimal.Create(config, other));
        }

        [RubyMethod("-")]
        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Subtract(config, self, BigDecimal.Create(config, other));
        }

        [RubyMethod("-")]
        [RubyMethod("sub")]
        public static object Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Subtract);
        }

        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other, int n) {
            return BigDecimal.Subtract(GetConfig(context), self, other, n);
        }

        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Subtract(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Subtract(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("sub")]
        public static BigDecimal/*!*/ Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Subtract(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("sub")]
        public static object Subtract(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other, [DefaultProtocol]int n) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Subtract);
        }

        #endregion

        #region mult, *

        [RubyMethod("*")]
        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other) {
            return BigDecimal.Multiply(GetConfig(context), self, other);
        }

        [RubyMethod("*")]
        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Multiply(config, self, BigDecimal.Create(config, other));
        }

        [RubyMethod("*")]
        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Multiply(config, self, BigDecimal.Create(config, other));
        }

        [RubyMethod("*")]
        [RubyMethod("mult")]
        public static object Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Multiply);
        }

        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other, int n) {
            return BigDecimal.Multiply(GetConfig(context), self, other, n);
        }

        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Multiply(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Multiply(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("mult")]
        public static BigDecimal/*!*/ Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other, int n) {
            BigDecimal.Config config = GetConfig(context);
            return BigDecimal.Multiply(config, self, BigDecimal.Create(config, other), n);
        }

        [RubyMethod("mult")]
        public static object Multiply(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other, [DefaultProtocol]int n) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Multiply);
        }

        #endregion

        [RubyMethod("/")]
        [RubyMethod("quo")]
        public static BigDecimal/*!*/ Divide(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other) {
            BigDecimal remainder;
            return BigDecimal.Divide(GetConfig(context), self, other, 0, out remainder);
        }

        [RubyMethod("div")]
        public static BigDecimal/*!*/ Div(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other) {
            if (BigDecimal.IsFinite(other)) {
                BigDecimal.Config config = GetConfig(context);
                BigDecimal remainder;
                BigDecimal result = BigDecimal.Divide(config, self, other, 0, out remainder);
                if (BigDecimal.IsFinite(result)) {
                    return BigDecimal.IntegerPart(config, result);
                }
            }
            return BigDecimal.NaN;
        }

        [RubyMethod("div")]
        public static BigDecimal/*!*/ Div(RubyContext/*!*/ context, BigDecimal/*!*/ self, BigDecimal/*!*/ other, int n) {
            if (n < 0) {
                throw RubyExceptions.CreateArgumentError("argument must be positive");
            }
            BigDecimal remainder;
            return BigDecimal.Divide(GetConfig(context), self, other, n, out remainder);
        }

        #region %, modulo

        [RubyMethod("%")]
        [RubyMethod("modulo")]
        public static BigDecimal/*!*/ Modulo(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            BigDecimal div, mod;
            BigDecimal.DivMod(GetConfig(context), self, other, out div, out mod);
            return mod;
        }

        [RubyMethod("%")]
        [RubyMethod("modulo")]
        public static BigDecimal/*!*/ Modulo(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            return Modulo(context, self, BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("%")]
        [RubyMethod("modulo")]
        public static BigDecimal/*!*/ Modulo(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            return Modulo(context, self, BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("modulo")]
        public static object Modulo(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            return LibrarySites.Modulo(context, BigDecimal.ToFloat(GetConfig(context), self), other);
        }

        [RubyMethod("%")]
        public static object ModuloOp(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.ModuloOp);
        }

        [RubyMethod("modulo")]
        public static object Modulo(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Modulo);
        }

        #endregion

        [RubyMethod("**")]
        [RubyMethod("power")]
        public static BigDecimal/*!*/ Power(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            return BigDecimal.Power(GetConfig(context), self, other);
        }

        [RubyMethod("+@")]
        public static BigDecimal/*!*/ Identity(BigDecimal/*!*/ self) {
            return self;
        }

        [RubyMethod("-@")]
        public static BigDecimal/*!*/ Negate(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            return BigDecimal.Negate(GetConfig(context), self);
        }

        [RubyMethod("abs")]
        public static BigDecimal/*!*/ Abs(RubyContext/*!*/ context, BigDecimal/*!*/ self) {
            return BigDecimal.Abs(GetConfig(context), self);
        }

        #region divmod

        [RubyMethod("divmod")]
        public static RubyArray/*!*/ DivMod(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            BigDecimal div, mod;
            BigDecimal.DivMod(GetConfig(context), self, other, out div, out mod);
            return RubyOps.MakeArray2(div, mod);
        }

        [RubyMethod("divmod")]
        public static RubyArray/*!*/ DivMod(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            return DivMod(context, self, BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("divmod")]
        public static RubyArray/*!*/ DivMod(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            return DivMod(context, self, BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("divmod")]
        public static object DivMod(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.DivMod);
        }

        #endregion

        #region remainder

        [RubyMethod("remainder")]
        public static BigDecimal/*!*/ Remainder(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            BigDecimal mod = Modulo(context, self, other);
            if (self.Sign == other.Sign) {
                return mod;
            } else {
                return BigDecimal.Subtract(GetConfig(context), mod, other);
            }
        }

        [RubyMethod("remainder")]
        public static object Remainder(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCall(context, self, other, LibrarySites.Remainder);
        }

        #endregion

        [RubyMethod("sqrt")]
        public static BigDecimal/*!*/ SquareRoot(RubyContext/*!*/ context, BigDecimal/*!*/ self, int n) {
            return BigDecimal.SquareRoot(GetConfig(context), self, n);
        }

        [RubyMethod("sqrt")]
        public static object SquareRoot(RubyContext/*!*/ context, BigDecimal/*!*/ self, object n) {
            throw RubyExceptions.CreateTypeError("wrong argument type " + RubyUtils.GetClassName(context, n) + " (expected Fixnum)");
        }

        #endregion

        #region Comparison Operations

        #region <=>
         
        [RubyMethod("<=>")]
        public static object Compare(BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            return self.CompareBigDecimal(other);
        }

        [RubyMethod("<=>")]
        public static object Compare(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            return self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("<=>")]
        public static object Compare(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            return self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("<=>")]
        public static object Compare(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            return self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("<=>")]
        public static object Compare(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            return Protocols.CoerceAndCallCompare(context, self, other);
        }

        #endregion

        #region >

        [RubyMethod(">")]
        public static object GreaterThan(BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            int? c = self.CompareBigDecimal(other);
            if (c.HasValue) {
                return c.Value > 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">")]
        public static object GreaterThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value > 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">")]
        public static object GreaterThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value > 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">")]
        public static object GreaterThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value > 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">")]
        public static object GreaterThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            int? c = Protocols.CoerceAndCallCompare(context, self, other) as int?;
            if (c.HasValue) {
                return c.Value > 0;
            } else {
                return null;
            }
        }

        #endregion

        #region >=

        [RubyMethod(">=")]
        public static object GreaterThanOrEqual(BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            int? c = self.CompareBigDecimal(other);
            if (c.HasValue) {
                return c.Value >= 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">=")]
        public static object GreaterThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value >= 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">=")]
        public static object GreaterThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value >= 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">=")]
        public static object GreaterThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value >= 0;
            } else {
                return null;
            }
        }

        [RubyMethod(">=")]
        public static object GreaterThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            int? c = Protocols.CoerceAndCallCompare(context, self, other) as int?;
            if (c.HasValue) {
                return c.Value >= 0;
            } else {
                return null;
            }
        }

        #endregion

        #region <

        [RubyMethod("<")]
        public static object LessThan(BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            int? c = self.CompareBigDecimal(other);
            if (c.HasValue) {
                return c.Value < 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<")]
        public static object LessThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value < 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<")]
        public static object LessThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value < 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<")]
        public static object LessThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value < 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<")]
        public static object LessThan(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            int? c = Protocols.CoerceAndCallCompare(context, self, other) as int?;
            if (c.HasValue) {
                return c.Value < 0;
            } else {
                return null;
            }
        }

        #endregion

        #region <=
        [RubyMethod("<=")]
        public static object LessThanOrEqual(BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            int? c = self.CompareBigDecimal(other);
            if (c.HasValue) {
                return c.Value <= 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<=")]
        public static object LessThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value <= 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<=")]
        public static object LessThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value <= 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<=")]
        public static object LessThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            int? c = self.CompareBigDecimal(BigDecimal.Create(GetConfig(context), other));
            if (c.HasValue) {
                return c.Value <= 0;
            } else {
                return null;
            }
        }

        [RubyMethod("<=")]
        public static object LessThanOrEqual(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            int? c = Protocols.CoerceAndCallCompare(context, self, other) as int?;
            if (c.HasValue) {
                return c.Value <= 0;
            } else {
                return null;
            }
        }

        #endregion

        [RubyMethod("eql?")]
        [RubyMethod("==")]
        [RubyMethod("===")]
        public static object Equal(BigDecimal/*!*/ self, [NotNull]BigDecimal/*!*/ other) {
            // This is a hack since normal numeric values return false not nil for NaN comparison
            if (BigDecimal.IsNaN(self) || BigDecimal.IsNaN(other)) {
                return null;
            }
            return self.Equals(other);
        }

        [RubyMethod("eql?")]
        [RubyMethod("==")]
        [RubyMethod("===")]
        public static bool Equal(RubyContext/*!*/ context, BigDecimal/*!*/ self, int other) {
            return self.Equals(BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("eql?")]
        [RubyMethod("==")]
        [RubyMethod("===")]
        public static bool Equal(RubyContext/*!*/ context, BigDecimal/*!*/ self, [NotNull]BigInteger/*!*/ other) {
            return self.Equals(BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("eql?")]
        [RubyMethod("==")]
        [RubyMethod("===")]
        public static bool Equal(RubyContext/*!*/ context, BigDecimal/*!*/ self, double other) {
            return self.Equals(BigDecimal.Create(GetConfig(context), other));
        }

        [RubyMethod("eql?")] // HACK - this is actually semantically wrong but is what the BigDecimal library does.
        [RubyMethod("==")]
        [RubyMethod("===")]
        public static object Equal(RubyContext/*!*/ context, BigDecimal/*!*/ self, object other) {
            // This is a hack since normal numeric values do not return nil for nil (they return false)
            if (other == null) {
                return null;
            }
            return Protocols.IsEqual(context, other, self);
        }

        #endregion

        #region Rounding Operations

        [RubyMethod("ceil")]
        public static BigDecimal/*!*/ Ceil(RubyContext/*!*/ context, BigDecimal/*!*/ self, [Optional]int n) {
            return BigDecimal.LimitPrecision(GetConfig(context), self, n, BigDecimal.RoundingModes.Ceiling);
        }

        [RubyMethod("floor")]
        public static BigDecimal/*!*/ Floor(RubyContext/*!*/ context, BigDecimal/*!*/ self, [Optional]int n) {
            return BigDecimal.LimitPrecision(GetConfig(context), self, n, BigDecimal.RoundingModes.Floor);
        }

        [RubyMethod("round")]
        public static BigDecimal/*!*/ Round(RubyContext/*!*/ context, BigDecimal/*!*/ self, [Optional]int n) {
            return BigDecimal.LimitPrecision(GetConfig(context), self, n, GetConfig(context).RoundingMode);
        }

        [RubyMethod("round")]
        public static BigDecimal/*!*/ Round(RubyContext/*!*/ context, BigDecimal/*!*/ self, int n, int mode) {
            return BigDecimal.LimitPrecision(GetConfig(context), self, n, (BigDecimal.RoundingModes)mode);
        }

        [RubyMethod("truncate")]
        public static BigDecimal/*!*/ Truncate(RubyContext/*!*/ context, BigDecimal/*!*/ self, [Optional]int n) {
            return BigDecimal.LimitPrecision(GetConfig(context), self, n, BigDecimal.RoundingModes.Down);
        }

        #endregion

        #region Tests

        [RubyMethod("finite?")]
        public static bool IsFinite(BigDecimal/*!*/ self) {
            return BigDecimal.IsFinite(self);
        }

        [RubyMethod("infinite?")]
        public static object IsInfinite(BigDecimal/*!*/ self) {
            if (BigDecimal.IsInfinite(self)) {
                return self.Sign;
            } else {
                return null;
            }
        }

        [RubyMethod("nan?")]
        public static bool IsNaN(BigDecimal/*!*/ self) {
            return BigDecimal.IsNaN(self);
        }

        [RubyMethod("nonzero?")]
        public static BigDecimal IsNonZero(BigDecimal/*!*/ self) {
            if (!BigDecimal.IsZero(self)) {
                return self;
            } else {
                return null;
            }
        }

        [RubyMethod("zero?")]
        public static bool IsZero(BigDecimal/*!*/ self) {
            return BigDecimal.IsZero(self);
        }

        #endregion

        #endregion
    }
}