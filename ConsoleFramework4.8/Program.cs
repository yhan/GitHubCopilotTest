using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleFramework4._8;

internal class Program
{
    private const int StackallocSize = 1024;
    public static void Main(string[] args)
    {
        List<int[]> mementos = new List<int[]>();
        while (true)
        {
            Task.Run(() => Allocate(mementos, sleepMs: 1));
            Task.Run(() =>
            {
                return StackAlloc();
                static Task StackAlloc()
                {
                    unsafe
                    {
                        int* numbers = stackalloc int[StackallocSize];
                        for (int i = 0; i < StackallocSize; i++) numbers[i] = i * i;
                        for (int i = 0; i < StackallocSize; i++) Console.WriteLine(numbers[i]);
                        return Task.CompletedTask;
                    }
                }
            });
            Allocate(mementos, 1000);
        }
    }

    static void Allocate(List<int[]> intsList, int sleepMs)
    {
        int[] array = new int[StackallocSize * 1024];
        intsList.Add(array);
        Thread.Sleep(sleepMs);
        Console.WriteLine($"List contains {intsList.Count} items");
    }
}