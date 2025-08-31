public static class ShmNames
{
    public static string Map(string baseName) => $"{baseName}.mmf";
    public static string Items(string baseName) => $"{baseName}.items";      // filled slots
    public static string Free(string baseName) => $"{baseName}.free";        // free slots
    public static string InitMutex(string baseName) => $"{baseName}.initmutex";
}