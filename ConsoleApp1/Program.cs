// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices.JavaScript;

const int StackallocSize = 1024;
List<int[]> mementos = new List<int[]>();

Console.WriteLine("Hello, World!");
Console.WriteLine($"gcMode=Servver ?  {GCSettings.IsServerGC} latencyMode={GCSettings.LatencyMode}");
int loop = 0;
DateTime start = DateTime.Now;
while (true)
{
    Task.Run(() => Allocate(mementos, sleepMs: 1));
    Task.Run(() =>
    {
        return StackAlloc();

        static Task StackAlloc()
        {
            Span<int> numbers = stackalloc int[StackallocSize];
            for (int i = 0; i < numbers.Length; i++) numbers[i] = i * i;
            // foreach (int n in numbers) Console.WriteLine(n);
            return Task.CompletedTask;
        }
    });
    Allocate(mementos, 1000);
    loop++;
}

void Allocate(List<int[]> intsList, int sleepMs)
{
    int[] array = new int[StackallocSize * 1024];
    intsList.Add(array);
    Thread.Sleep(sleepMs);
    // if(DateTime.Now.Subtract(start) > TimeSpan.FromSeconds(10))
    //     Debugger.Break();
    // Console.WriteLine($"List contains {intsList.Count} items");
}