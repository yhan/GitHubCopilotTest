// BubbleSortTests.cs
using NUnit.Framework;

namespace TestProjectNet8;

public class BubbleSortTests
{
    [Test]
    public void BubbleSort_SortsArrayCorrectly()
    {
        int[] input = { 5, 3, 8, 4, 2 };
        int[] expected = { 2, 3, 4, 5, 8 };

        BubbleSort(input); // BubbleSort.cs
        Assert.That(input, Is.EqualTo(expected));
    }

    public static void BubbleSort(int[] arr)
    {
        for (int i = 0; i < arr.Length - 1; i++)
        {
            for (int j = 0; j < arr.Length - i - 1; j++)
            {
                if (arr[j] > arr[j + 1])
                {
                    (arr[j], arr[j + 1]) = (arr[j + 1], arr[j]);
                }
            }
        }
    }

    [Test]
    public void BubbleSort_EmptyArray_NoChange()
    {
        int[] input = { };
        int[] expected = { };

        BubbleSort(input);

        Assert.That(input, Is.EqualTo(expected));
    }
}