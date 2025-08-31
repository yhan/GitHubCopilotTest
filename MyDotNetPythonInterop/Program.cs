// using System;
// using Python.Runtime;
//
// class Program
// {
//     static void Main()
//     {
//         PythonEngine.Initialize();
//
//         using (Py.GIL())
//         {
//             dynamic np = Py.Import("numpy");
//             dynamic functions = Py.Import("functions"); // functions.py must be in current directory or sys.path
//
//             // Create a NumPy array in C#
//             dynamic mid = np.array(new float[] { 100.0f, 101.0f, 102.0f });
//
//             // Call the function
//             dynamic result = functions.simulate_bid_ask(mid);
//             dynamic bid = result[0];
//             dynamic ask = result[1];
//
//             Console.WriteLine($"Bid[0]: {bid[0]}");
//             Console.WriteLine($"Ask[0]: {ask[0]}");
//         }
//
//         PythonEngine.Shutdown();
//     }
// }