using System;
using System.Diagnostics;
using System.Reflection;

namespace Chroma64.Util
{
    static class Log
    {
        public static void Info(string msg)
        {
            Console.WriteLine($"[{NameOfCallingClass()}] {msg}");
        }

        public static void Warning(string msg)
        {
            Console.WriteLine($"[{NameOfCallingClass()} - WARN] {msg}");
        }

        public static void Error(string msg)
        {
            Console.Error.WriteLine($"[{NameOfCallingClass()} - ERROR] {msg}");
        }

        public static void CriticalError(string msg)
        {
            Console.Error.WriteLine($"[{NameOfCallingClass()} - CRIT] {msg}");
            Console.Error.WriteLine($"Press any key to continue.");
            Console.ReadKey();
        }

        public static void FatalError(string msg)
        {
            Console.Error.WriteLine($"[{NameOfCallingClass()} - FATAL] {msg}");
            Console.Error.WriteLine($"Press any key to exit.");
            Console.ReadKey();
            Environment.Exit(-1);
        }

        private static string NameOfCallingClass()
        {
            string fullName;
            Type declaringType;
            int skipFrames = 2;
            do
            {
                MethodBase method = new StackFrame(skipFrames, false).GetMethod();
                declaringType = method.DeclaringType;
                if (declaringType == null)
                {
                    return method.Name;
                }
                skipFrames++;
                fullName = declaringType.FullName;
            }
            while (declaringType.Module.Name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase));

            return fullName.Substring(fullName.LastIndexOf('.') + 1);
        }
    }
}
