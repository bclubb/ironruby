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

using System.Collections;
using System.Collections.Generic;
using Microsoft.Scripting.Utils;
using IronRuby.Runtime;
using System.Diagnostics;
using System.Text;
using Microsoft.Scripting.Runtime;

namespace IronRuby.Builtins {

    /// <summary>
    /// Array inherits from Object, mixes in Enumerable.
    /// Ruby's Array is a List{object}, but it overrides Equals and GetHashCode so
    /// .NET dictionaries will hash it correctly.
    /// </summary>
    [DebuggerDisplay("{GetDebugView()}")]
    public partial class RubyArray : List<object>, IDuplicable {
        #region Construction

        public RubyArray() {
        }

        public RubyArray(int capacity)
            : base(capacity) {
        }

        public RubyArray(IEnumerable<object>/*!*/ collection)
            : base(collection) {
        }

        public RubyArray(IEnumerable/*!*/ collection)
            : base(CollectionUtils.ToEnumerable<object>(collection)) {
        }

        public static RubyArray/*!*/ Create<T>(IList<T>/*!*/ list, int start, int count) {
            ContractUtils.RequiresNotNull(list, "list");
            ContractUtils.RequiresArrayRange(list, start, count, "start", "count");

            RubyArray result = new RubyArray();
            for (int i = 0; i < count; i++) {
                result.Add(list[start + i]);
            }
            return result;
        }

        public static RubyArray/*!*/ Create(object item) {
            var result = new RubyArray();
            result.Add(item);
            return result;
        }

        /// <summary>
        /// Creates a blank instance of a RubyArray or its subclass given the Ruby class object.
        /// </summary>
        public static RubyArray/*!*/ CreateInstance(RubyClass/*!*/ rubyClass) {
            return (rubyClass.GetUnderlyingSystemType() == typeof(RubyArray)) ? new RubyArray() : new RubyArray.Subclass(rubyClass);
        }

        /// <summary>
        /// Creates a blank instance of self type with no flags set.
        /// </summary>
        public virtual RubyArray/*!*/ CreateInstance() {
            return new RubyArray();
        }

        /// <summary>
        /// Creates a copy of the array that has the same items.
        /// Doesn't copy instance data.
        /// Preserves the class of the Array.
        /// </summary>
        protected virtual RubyArray/*!*/ Copy() {
            return new RubyArray(this);
        }

        object IDuplicable.Duplicate(RubyContext/*!*/ context, bool copySingletonMembers) {
            var result = Copy();
            context.CopyInstanceData(this, result, copySingletonMembers);
            return result;
        }

        #endregion

        public override bool Equals(object obj) {
            return Equals(this, obj);
        }

        public override int GetHashCode() {
            return GetHashCode(this);
        }

        public static int GetHashCode(IList/*!*/ self) {
            int hash = self.Count;
            foreach (object obj in self) {
                hash <<= 1;
                hash ^= RubyUtils.GetHashCode(obj);
            }
            return hash;
        }

        public static bool Equals(IList/*!*/ self, object obj) {
            if (self == obj) {
                return true;
            }

            IList other = obj as IList;
            if (other == null || self.Count != other.Count) {
                return false;
            }

            for (int i = 0; i < self.Count; ++i) {
                if (!RubyUtils.ValueEquals(self[i], other[i])) {
                    return false;
                }
            }
            return true;
        }

        public RubyArray/*!*/ AddMultiple(int count, object value) {
            for (int i = 0; i < count; i++) {
                Add(value);
            }
            return this;
        }

        #region DebugView

        internal string/*!*/ GetDebugView() {
            return RubySites.Inspect(RubyContext._Default, this).ToString();
        }

        #endregion
    }
}
