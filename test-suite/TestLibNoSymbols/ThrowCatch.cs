using System;

namespace TestLibNoSymbols
{
    public class ThrowCatch
    {
        // Throws and handles internally: a debugger breaking on all thrown
        // exceptions stops on the throw line of this symbol-less module.
        public static int Run()
        {
            try
            {
                throw new FormatException("handled inside the library");
            }
            catch (FormatException)
            {
                return 1;
            }
        }
    }
}
