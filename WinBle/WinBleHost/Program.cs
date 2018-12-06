using System;

namespace BleSock.Windows
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                int portNumber = int.Parse(args[1]);

                if (args[0] == "Central")
                {
                    using (var centralImpl = new CentralImpl())
                    {
                        centralImpl.Run(portNumber);
                    }
                }
                else if (args[0] == "Peripheral")
                {
                    using (var peripheralImpl = new PeripheralImpl())
                    {
                        peripheralImpl.Run(portNumber);
                    }
                }
                else
                {
                    Utils.Error("Invalid argument: {0}", args[0]);
                }
            }
            catch (Exception e)
            {
                Utils.Error(e.ToString());
            }

            Console.ReadKey();
        }
    }
}
