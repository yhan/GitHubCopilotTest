using Microsoft.Diagnostics.Tracing;
using System;

class Program
{
    static void Main(string[] args)
    {
        string etlFileName = "ETWData.etl";
        if(args.Length >= 1)
            etlFileName = args[0];
        
        using (var source = new ETWTraceEventSource(etlFileName))
        {
            // setup the callbacks
            source.Clr.All += Print;
            source.Kernel.All += Print;
            source.Dynamic.All += Print;

            // iterate over the file, calling the callbacks.
            source.Process();
        }
    }

    private static void Print(TraceEvent data)
    {
        Console.WriteLine(data.ToString());
    }
}