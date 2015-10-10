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
using System.Text;

namespace Patcher.Logging
{
    public class StreamLogger : Logger, IDisposable
    {
        TextWriter writer;

        Dictionary<LogLevel, string> logLevelNameMap = new Dictionary<LogLevel, string>()
        {
            { LogLevel.None, "NONE " },
            { LogLevel.Error, "ERROR" },
            { LogLevel.Warning, "WARN " },
            { LogLevel.Info, "INFO " },
            { LogLevel.Fine, "FINE " }
        };

        public StreamLogger(Stream logFileStream)
        {
            writer = new StreamWriter(logFileStream);
        }

        public void Dispose()
        {
            writer.Dispose();
        }

        internal override LogLevel MaxLogLevel { get { return LogLevel.Fine; } } 

        internal override void WriteLogEntry(LogEntry entry)
        {
            writer.WriteLine("{0} {1} [{2}.{3}] {4}", 
                DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.ffffff"), 
                logLevelNameMap[entry.Level], 
                entry.Caller.DeclaringType.FullName, 
                entry.Caller.Name, 
                entry.Text);
        }
    }
}