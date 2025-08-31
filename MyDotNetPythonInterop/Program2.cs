using Python.Runtime;

Runtime.PythonDLL = @"C:\Users\hanyi\apps\python-3.10.0-embed-amd64\python310.dll";
PythonEngine.PythonHome = @"C:\Users\hanyi\apps\python-3.10.0-embed-amd64";
// Initialize Python
PythonEngine.Initialize();


string currentDir = System.IO.Directory.GetCurrentDirectory();
using (Py.GIL())
{
    dynamic sys = Py.Import("sys");
    sys.path.append(currentDir);

    dynamic functions = Py.Import("functions");
}
PythonEngine.Shutdown();