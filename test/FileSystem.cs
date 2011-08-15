/*
 * libchmsharp2 - a C# port of chmlib
 * Copyright (C) 2011 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace test
{
    /// <summary>
    /// Extensions for file system operations.
    /// </summary>
    public static class FileSystem
    {
        /// <summary>
        /// Recursively creates a directory tree.
        /// </summary>
        /// <param name="di">the deepest directory node in the path</param>
        /// <param name="created">output list of directories created</param>
        public static void CreateDirectory(this DirectoryInfo di, out List<DirectoryInfo> created)
        {
            if (di.Parent != null)
                FileSystem.CreateDirectory(di.Parent, out created);
            else
                created = new List<DirectoryInfo>();

            if (!di.Exists) {
                System.IO.Directory.CreateDirectory(di.FullName);
                created.Add(di);
            }
        }
    }
}
