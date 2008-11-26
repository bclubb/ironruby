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
using System.Dynamic;
using Microsoft.Scripting;
using MSA = System.Linq.Expressions;
using AstUtils = Microsoft.Scripting.Ast.Utils;

namespace IronRuby.Compiler.Ast {
    using Ast = System.Linq.Expressions.Expression;
    using Microsoft.Scripting.Utils;

    public abstract class Variable : LeftValue {
        private readonly string/*!*/ _name;

        public string/*!*/ Name {
            get { return _name; }
        }

        public Variable(string/*!*/ name, SourceSpan location)
            : base(location) {
            ContractUtils.RequiresNotNull(name, "name");

            _name = name;
        }

        internal MSA.Expression/*!*/ TransformName(AstGenerator/*!*/ gen) {
            return Ast.Constant(_name);
        }

        internal sealed override MSA.Expression TransformTargetRead(AstGenerator/*!*/ gen) {
            return null;
        }

        internal abstract MSA.Expression/*!*/ TransformReadVariable(AstGenerator/*!*/ gen, bool tryRead);
        internal abstract MSA.Expression/*!*/ TransformWriteVariable(AstGenerator/*!*/ gen, MSA.Expression/*!*/ rightValue);

        internal override MSA.Expression/*!*/ TransformRead(AstGenerator/*!*/ gen, MSA.Expression targetValue, bool tryRead) {
            Debug.Assert(targetValue == null);
            return TransformReadVariable(gen, tryRead);
        }

        internal override MSA.Expression/*!*/ TransformWrite(AstGenerator/*!*/ gen, MSA.Expression targetValue, MSA.Expression/*!*/ rightValue) {
            Debug.Assert(targetValue == null);
            return TransformWriteVariable(gen, rightValue);
        }

        public override string/*!*/ ToString() {
            return _name.ToString();
        }
    }
}
