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

#if !SILVERLIGHT

using System.Globalization;
using System.Linq.Expressions;
using System.Dynamic.Utils;

namespace System.Dynamic.ComInterop {

    internal class ConvertibleArgBuilder : ArgBuilder {
        internal ConvertibleArgBuilder() {
        }

        internal override Expression Marshal(Expression parameter) {
            return Helpers.Convert(parameter, typeof(IConvertible));
        }

        internal override Expression MarshalToRef(Expression parameter) {
            //we are not supporting convertible InOut
            throw Assert.Unreachable;
        }
    }
}

#endif
