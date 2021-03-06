﻿/// Copyright(C) 2015 Unforbidable Works
///
/// This program is free software; you can redistribute it and/or
/// modify it under the terms of the GNU General Public License
/// as published by the Free Software Foundation; either version 2
/// of the License, or(at your option) any later version.
///
/// This program is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
/// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
/// GNU General Public License for more details.
///
/// You should have received a copy of the GNU General Public License
/// along with this program; if not, write to the Free Software
/// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Patcher.Rules.Compiled.Fields.Skyrim;
using Patcher.Data.Plugins.Content.Fields.Skyrim;
using Patcher.Rules.Proxies.Fields.Skyrim;
using Patcher.Rules.Proxies;
using Patcher.Data.Plugins.Content.Constants.Skyrim;
using Patcher.Rules.Proxies.Forms;
using Patcher.Data.Plugins.Content.Functions.Skyrim;
using Patcher.Rules.Compiled.Forms;
using Patcher.Rules.Compiled.Forms.Skyrim;

namespace Patcher.Rules.Compiled.Helpers.Skyrim
{
    [Helper("Functions", typeof(IFunctionsHelper))]
    sealed class FunctionsHelper : IFunctionsHelper
    {
        readonly CompiledRuleContext context;

        public FunctionsHelper(CompiledRuleContext context)
        {
            this.context = context;
        }

        public ICondition EPTemperingItemIsEnchanted()
        {
            return CreateConditionProxy(Function.EPTemperingItemIsEnchanted);
        }

        public ICondition GenericFunction(int number)
        {
            return CreateConditionProxy((Function)number).CheckParams();
        }

        public ICondition GenericFunction(int number, object paramA)
        {
            return CreateConditionProxy((Function)number).SetParam(0, paramA).CheckParams();
        }

        public ICondition GenericFunction(int number, object paramA, object paramB)
        {
            return CreateConditionProxy((Function)number).SetParam(0, paramA).SetParam(1, paramB).CheckParams();
        }

        public ICondition GetCurrentTime()
        {
            return CreateConditionProxy(Function.GetCurrentTime);
        }

        public ICondition GetGlobalValue(IForm global)
        {
            return CreateConditionProxy(Function.GetGlobalValue).SetParam(0, global);
        }

        public ICondition GetInCurrentLoc(IForm location)
        {
            return CreateConditionProxy(Function.GetInCurrentLoc).SetParam(0, location);
        }

        public ICondition GetItemCount(IForm item)
        {
            return CreateConditionProxy(Function.GetItemCount).SetParam(0, item);
        }

        public ICondition GetQuestCompleted(IForm quest)
        {
            return CreateConditionProxy(Function.GetQuestCompleted).SetParam(0, quest);
        }

        public ICondition GetStageDone(IForm quest, int stage)
        {
            return CreateConditionProxy(Function.GetStageDone).SetParam(0, quest).SetParam(1, stage);
        }

        public ICondition GetVMQuestVariable(IForm quest, string variable)
        {
            return CreateConditionProxy(Function.GetVMQuestVariable).SetParam(0, quest).SetParam(1, variable);
        }

        public ICondition HasKeyword(IForm keyword)
        {
            return CreateConditionProxy(Function.HasKeyword).SetParam(0, keyword);
        }

        public ICondition HasPerk(IForm perk)
        {
            return CreateConditionProxy(Function.HasPerk).SetParam(0, perk);
        }

        private ConditionProxy CreateConditionProxy(Function number)
        {
            if (!Enum.IsDefined(typeof(Function), number))
                Log.Warning("Unrecognized function '{0}' will be treated as a parameterless function and assigning arguments will produce warnings.", number);

            // Create proxy in Target mode so that it can be modified
            var proxy = context.Rule.Engine.ProxyProvider.CreateProxy<ConditionProxy>(ProxyMode.Target);
            proxy.Field = new Condition()
            {
                Function = number
            };
            return proxy;
        }
    }
}
