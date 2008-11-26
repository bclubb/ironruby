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

using System;
using IronRuby.Builtins;
using IronRuby.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting.Shell;
using Microsoft.Scripting.Runtime;

namespace IronRuby.Hosting {
   
    /// <summary>
    /// A simple Ruby command-line should mimic the standard irb.exe
    /// </summary>
    public class RubyCommandLine : CommandLine {
        public RubyCommandLine() {
        }

        protected override string Logo {
            get {
                return String.Format("IronRuby {1} on .NET {2}{0}Copyright (c) Microsoft Corporation. All rights reserved.{0}{0}Note that local variables do not work today in the console.{0}As a workaround, use globals instead (eg $x = 42 instead of x = 42).{0}{0}",
                    Environment.NewLine, RubyContext.IronRubyVersion, Environment.Version);
            }
        }

        protected override int? TryInteractiveAction() {
            try {
                return base.TryInteractiveAction();
            } catch (SystemExit e) {
                return e.Status;
            }
        }

        // overridden to set the default encoding to BINARY
        protected override int RunFile(string fileName) {
            return RunFile(Engine.CreateScriptSourceFromFile(fileName, BinaryEncoding.Instance));
        }

        protected override Scope/*!*/ CreateScope() {
            Scope scope = base.CreateScope();
            scope.SetName(SymbolTable.StringToId("iron_ruby"), Engine);
            return scope;
        }
    }
}
