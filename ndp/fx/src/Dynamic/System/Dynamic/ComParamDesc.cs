/* ****************************************************************************
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

#if !SILVERLIGHT // ComObject

using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Marshal = System.Runtime.InteropServices.Marshal;
using VarEnum = System.Runtime.InteropServices.VarEnum;

namespace System.Dynamic {

    /// <summary>
    /// The parameter description of a method defined in a type library
    /// </summary>
    internal sealed class ComParamDesc {
        # region private fields

        private readonly bool _isOut; // is an output parameter?
        private readonly bool _isOpt; // is an optional parameter?
        private readonly bool _byRef; // is a reference or pointer parameter?
        private readonly bool _isArray;
        private readonly VarEnum _vt;
        private readonly string _name;
        private readonly Type _type;
        private readonly object _defaultValue;

        # endregion

        /// <summary>
        /// Creates a representation for the paramter of a COM method
        /// </summary>
        internal ComParamDesc(ref ELEMDESC elemDesc, string name) {
            // Ensure _defaultValue is set to DBNull.Value regardless of whether or not the 
            // default value is extracted from the parameter description.  Failure to do so
            // yields a runtime exception in the ToString() function.
            _defaultValue = DBNull.Value;

            if (!String.IsNullOrEmpty(name)) {
                // This is a parameter, not a return value
                this._isOut = (elemDesc.desc.paramdesc.wParamFlags & PARAMFLAG.PARAMFLAG_FOUT) != 0;
                this._isOpt = (elemDesc.desc.paramdesc.wParamFlags & PARAMFLAG.PARAMFLAG_FOPT) != 0;
                _defaultValue = PARAMDESCEX.GetDefaultValue(ref elemDesc.desc.paramdesc);
            }

            _name = name;
            _vt = (VarEnum)elemDesc.tdesc.vt;
            TYPEDESC typeDesc = elemDesc.tdesc;
            while (true) {
                if (_vt == VarEnum.VT_PTR) {
                    this._byRef = true;
                } else if (_vt == VarEnum.VT_ARRAY) {
                    this._isArray = true;
                } else {
                    break;
                }

                TYPEDESC childTypeDesc = (TYPEDESC)Marshal.PtrToStructure(typeDesc.lpValue, typeof(TYPEDESC));
                _vt = (VarEnum)childTypeDesc.vt;
                typeDesc = childTypeDesc;
            }

            VarEnum vtWithoutByref = _vt;
            if ((_vt & VarEnum.VT_BYREF) != 0) {
                vtWithoutByref = (_vt & ~VarEnum.VT_BYREF);
                _byRef = true;
            }

            _type = GetTypeForVarEnum(vtWithoutByref);
        }

        internal struct PARAMDESCEX {
            private ulong _cByte;
            private Variant _varDefaultValue;

            internal void Dummy() {
                _cByte = 0;
                throw Error.MethodShouldNotBeCalled();
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
            internal static object GetDefaultValue(ref PARAMDESC paramdesc) {
                if ((paramdesc.wParamFlags & PARAMFLAG.PARAMFLAG_FHASDEFAULT) == 0) {
                    return DBNull.Value;
                }

                PARAMDESCEX varValue = (PARAMDESCEX)Marshal.PtrToStructure(paramdesc.lpVarValue, typeof(PARAMDESCEX));
                if (varValue._cByte != (ulong)(Marshal.SizeOf((typeof(PARAMDESCEX))))) {
                    return DBNull.Value;
                }

                try {
                    // this may fail for various reasons such as no managed representation for native type
                    return varValue._varDefaultValue.ToObject();
                } catch (Exception) {
                    return DBNull.Value;
                }
            }
        }

        private static Type GetTypeForVarEnum(VarEnum vt) {
            Type type;

            switch (vt) {
                // VarEnums which can be used in VARIANTs, but which cannot occur in a TYPEDESC
                case VarEnum.VT_EMPTY:
                case VarEnum.VT_NULL:
                case VarEnum.VT_RECORD:
                    throw Error.UnexpectedVarEnum(vt);

                // VarEnums which are not used in VARIANTs, but which can occur in a TYPEDESC
                case VarEnum.VT_VOID:
                    type = null;
                    break;

                case VarEnum.VT_HRESULT:
                    type = typeof(int);
                    break;

                case ((VarEnum)37): // VT_INT_PTR:
                case VarEnum.VT_PTR:
                    type = typeof(IntPtr);
                    break;

                case ((VarEnum)38): // VT_UINT_PTR:
                    type = typeof(UIntPtr);
                    break;

                case VarEnum.VT_SAFEARRAY:
                case VarEnum.VT_CARRAY:
                    type = typeof(Array);
                    break;

                case VarEnum.VT_LPSTR:
                case VarEnum.VT_LPWSTR:
                    type = typeof(string);
                    break;

                case VarEnum.VT_USERDEFINED:
                    type = typeof(object);
                    break;

                // For VarEnums that can be used in VARIANTs and well as TYPEDESCs, just use VarEnumSelector
                default:
                    type = VarEnumSelector.GetManagedMarshalType(vt);
                    break;
            }

            return type;
        }

        public override string ToString() {
            StringBuilder result = new StringBuilder();
            if (_isOpt) {
                result.Append("[Optional] ");
            }

            if (_isOut) {
                result.Append("[out]");
            }

            result.Append(_type.Name);

            if (_isArray) {
                result.Append("[]");
            }

            if (_byRef) {
                result.Append("&");
            }

            result.Append(" ");
            result.Append(_name);

            if (_defaultValue != DBNull.Value) {
                result.Append("=");
                result.Append(_defaultValue.ToString());
            }

            return result.ToString();
        }

        public bool IsOut {
            get { return _isOut; }
        }

        public bool ByReference {
            get { return _byRef; }
        }
    }
}

#endif
