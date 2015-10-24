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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Documenter
{
    static class Utility
    {
        public static string GetLocalNamespace(this Type type)
        {
            if (type.Namespace.StartsWith(Program.RootNamespace))
            {
                return type.Namespace.Substring(Program.RootNamespace.Length).TrimStart('.');
            }
            else
            {
                return type.Namespace;
            }
        }

        public static string GetLocalName(this Type type)
        {
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                return "delegate";
            }
            else if (type.FullName.Contains(".Helpers."))
            {
                return type.Name.TrimStart('I').Replace("Helper", string.Empty);
            }
            else if (type.FullName.Contains(".Forms.") || type.Namespace.Contains(".Fields."))
            {
                return type.Name.TrimStart('I').Replace("`1", string.Empty);
            }
            else if (type.FullName.StartsWith(Program.RootNamespace))
            {
                return type.Name;
            }
            else
            {
                switch (type.Name)
                {
                    case "Boolean":
                        return "bool";
                    case "Int32":
                        return "int";
                    case "UInt32":
                        return "uint";
                    case "Int16":
                        return "short";
                    case "UInt16":
                        return "ushort";
                    case "Single":
                        return "float";
                    default:
                        return type.Name.ToLower();
                }
            }
        }

        public static string GetLocalFullName(this Type type)
        {
            return string.Format("{0}.{1}", type.GetLocalNamespace(), type.GetLocalName());
        }

        public static string GetLocalPath(this Type type, string ext)
        {
            return type.GetLocalFullName().Replace('.', Path.DirectorySeparatorChar) + ext;
        }

        public static string GetMethodSignature(this MethodInfo method)
        {
            return string.Format("{0}({1})", method.Name, string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)));
        }

        public static string GetTypeReference(this Type type)
        {
            if (type.Namespace.Contains(Program.RootNamespace))
            {
                string generic = string.Empty;

                if (type.IsGenericType)
                {
                    var genericType = type.GetGenericArguments()[0];
                    if (!genericType.IsGenericParameter)
                        generic = " of " + GetTypeReference(genericType);
                }
                return string.Format("<see cref='{0}'>{1}</see>{2}", type.GetLocalFullName(), type.GetLocalName(), generic);
            }
            else
            {
                return string.Format("<c>{0}</c>", type.GetLocalName());
            }
        }
    }
}