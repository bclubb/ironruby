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
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Dynamic.Utils;

namespace System.Linq.Expressions {
    /// <summary>
    /// Represents initializing members of a member of a newly created object.
    /// </summary>
    /// <remarks>
    /// Use the <see cref="M:MemberBind"/> factory methods to create a <see cref="MemberMemberBinding"/>.
    /// The value of the <see cref="P:MemberBinding.BindingType"/> property of a <see cref="MemberMemberBinding"/> object is <see cref="MemberBinding"/>. 
    /// </remarks>
    public sealed class MemberMemberBinding : MemberBinding {
        ReadOnlyCollection<MemberBinding> _bindings;
        internal MemberMemberBinding(MemberInfo member, ReadOnlyCollection<MemberBinding> bindings)
            : base(MemberBindingType.MemberBinding, member) {
            _bindings = bindings;
        }

        /// <summary>
        /// Gets the bindings that describe how to initialize the members of a member. 
        /// </summary>
        public ReadOnlyCollection<MemberBinding> Bindings {
            get { return _bindings; }
        }
    }
    

    public partial class Expression {
        /// <summary>
        /// Creates a <see cref="MemberMemberBinding"/> that represents the recursive initialization of members of a field or property. 
        /// </summary>
        /// <param name="member">The <see cref="MemberInfo"/> to set the <see cref="P:MemberBinding.Member"/> property equal to.</param>
        /// <param name="bindings">An array of <see cref="MemberBinding"/> objects to use to populate the <see cref="P:MemberMemberBindings.Bindings"/> collection.</param>
        /// <returns>A <see cref="MemberMemberBinding"/> that has the <see cref="P:MemberBinding.BindingType"/> property equal to <see cref="MemberBinding"/> and the <see cref="P:MemberBinding.Member"/> and <see cref="P:MemberMemberBindings.Bindings"/> properties set to the specified values.</returns>
        public static MemberMemberBinding MemberBind(MemberInfo member, params MemberBinding[] bindings) {
            ContractUtils.RequiresNotNull(member, "member");
            ContractUtils.RequiresNotNull(bindings, "bindings");
            return MemberBind(member, bindings.ToReadOnly());
        }
        
        /// <summary>
        /// Creates a <see cref="MemberMemberBinding"/> that represents the recursive initialization of members of a field or property. 
        /// </summary>
        /// <param name="member">The <see cref="MemberInfo"/> to set the <see cref="P:MemberBinding.Member"/> property equal to.</param>
        /// <param name="bindings">An <see cref="IEnumerable{T}"/> that contains <see cref="MemberBinding"/> objects to use to populate the <see cref="P:MemberMemberBindings.Bindings"/> collection.</param>
        /// <returns>A <see cref="MemberMemberBinding"/> that has the <see cref="P:MemberBinding.BindingType"/> property equal to <see cref="MemberBinding"/> and the <see cref="P:MemberBinding.Member"/> and <see cref="P:MemberMemberBindings.Bindings"/> properties set to the specified values.</returns>
        public static MemberMemberBinding MemberBind(MemberInfo member, IEnumerable<MemberBinding> bindings) {
            ContractUtils.RequiresNotNull(member, "member");
            ContractUtils.RequiresNotNull(bindings, "bindings");
            ReadOnlyCollection<MemberBinding> roBindings = bindings.ToReadOnly();
            Type memberType;
            ValidateGettableFieldOrPropertyMember(member, out memberType);
            ValidateMemberInitArgs(memberType, roBindings);
            return new MemberMemberBinding(member, roBindings);
        }

        /// <summary>
        /// Creates a <see cref="MemberMemberBinding"/> that represents the recursive initialization of members of a member that is accessed by using a property accessor method.  
        /// </summary>
        /// <param name="propertyAccessor">The <see cref="MemberInfo"/> that represents a property accessor method.</param>
        /// <param name="bindings">An <see cref="IEnumerable{T}"/> that contains <see cref="MemberBinding"/> objects to use to populate the <see cref="P:MemberMemberBindings.Bindings"/> collection.</param>
        /// <returns>
        /// A <see cref="MemberMemberBinding"/> that has the <see cref="P:MemberBinding.BindingType"/> property equal to <see cref="MemberBinding"/>, 
        /// the Member property set to the <see cref="PropertyInfo"/> that represents the property accessed in <paramref name="propertyAccessor"/>, 
        /// and <see cref="P:MemberMemberBindings.Bindings"/> properties set to the specified values.
        /// </returns>
        public static MemberMemberBinding MemberBind(MethodInfo propertyAccessor, params MemberBinding[] bindings) {
            ContractUtils.RequiresNotNull(propertyAccessor, "propertyAccessor");
            return MemberBind(GetProperty(propertyAccessor), bindings);
        }

        /// <summary>
        /// Creates a <see cref="MemberMemberBinding"/> that represents the recursive initialization of members of a member that is accessed by using a property accessor method.  
        /// </summary>
        /// <param name="propertyAccessor">The <see cref="MemberInfo"/> that represents a property accessor method.</param>
        /// <param name="bindings">An <see cref="IEnumerable{T}"/> that contains <see cref="MemberBinding"/> objects to use to populate the <see cref="P:MemberMemberBindings.Bindings"/> collection.</param>
        /// <returns>
        /// A <see cref="MemberMemberBinding"/> that has the <see cref="P:MemberBinding.BindingType"/> property equal to <see cref="MemberBinding"/>, 
        /// the Member property set to the <see cref="PropertyInfo"/> that represents the property accessed in <paramref name="propertyAccessor"/>, 
        /// and <see cref="P:MemberMemberBindings.Bindings"/> properties set to the specified values.
        /// </returns>
        public static MemberMemberBinding MemberBind(MethodInfo propertyAccessor, IEnumerable<MemberBinding> bindings) {
            ContractUtils.RequiresNotNull(propertyAccessor, "propertyAccessor");
            return MemberBind(GetProperty(propertyAccessor), bindings);
        }

        private static void ValidateGettableFieldOrPropertyMember(MemberInfo member, out Type memberType) {
            FieldInfo fi = member as FieldInfo;
            if (fi == null) {
                PropertyInfo pi = member as PropertyInfo;
                if (pi == null) {
                    throw Error.ArgumentMustBeFieldInfoOrPropertInfo();
                }
                if (!pi.CanRead) {
                    throw Error.PropertyDoesNotHaveGetter(pi);
                }
                memberType = pi.PropertyType;
            } else {
                memberType = fi.FieldType;
            }
        }
        
        private static void ValidateMemberInitArgs(Type type, ReadOnlyCollection<MemberBinding> bindings) {
            for (int i = 0, n = bindings.Count; i < n; i++) {
                MemberBinding b = bindings[i];
                ContractUtils.RequiresNotNull(b, "bindings");
                if (!b.Member.DeclaringType.IsAssignableFrom(type)) {
                    throw Error.NotAMemberOfType(b.Member.Name, type);
                }
            }
        }
    }
}