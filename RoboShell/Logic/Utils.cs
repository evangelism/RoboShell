using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoboShell.Logic
{
    public static class Utils
    {
        public static MemoryStream NewStream(this Stream s)
        {
            var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0L;
            return ms;
        }
    }
}
