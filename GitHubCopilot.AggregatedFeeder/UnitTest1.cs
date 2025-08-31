using NFluent;

namespace TestProjectNet8;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {
       // Span<Person> span = stackalloc Person[10];  // This is by design to prevent nested ref struct lifetimes, which are hard to track safely.
    }

    [Test]
    [Repeat(1000)]
    public unsafe void Test2()
    {
        // PersonRefStruct* people = stackalloc PersonRefStruct[10];
        // people[0].Age = 42;
        //
        // people[111].Age = 111;  // illegal should not do this But you are corrupting stack memory — this is just luck, not correctness.
        /*
         * This is undefined behavior — it may:

Crash (stack corruption)

Overwrite return addresses, locals, or parameters

Quietly work until it suddenly doesn't
         */
        // Check.That(people[111].Age).IsEqualTo(111);
        // Console.WriteLine($"passed : {TestContext.CurrentContext.CurrentRepeatCount}");
    }

    [Test]
    public void test()
    {
        var phrase = "code: blablabla";
        int separatorIndex = phrase.IndexOf(':');
        
        Check.That(phrase.Substring(separatorIndex+2)).IsEqualTo("blablabla");

        /*
         * [ Stack Frame ]
            | persons (Span<Person2>) |
            | Person2[0]              |
            | Person2[1]              |
            | ...                     |
            | Person2[9]              |
         */
        /*
         *
         *
         * A stackalloc expression produces a raw native pointer to the newly?allocated block.
The garbage?collector never sees that memory.

If the element type contained GC references, the GC wouldn’t know about them and could move or collect the objects they point to, leaving invalid pointers on the stack.

To prevent that unsoundness the language requires that T be unmanaged – that is, the struct (recursively) contains only value?type fields with no object references. 
Microsoft Learn

(The same rule explains the more general pointer error shown in the tooltip. 
error! Span<Person2> people1 = stackalloc Person2[10];
         */
        
        Span<PersonStruct> people2 = new Span<PersonStruct>(new PersonStruct[10]); // Span is a pointer to an underlying data ( allocated on heap or stack) with a length

        //Span<PersonRefStruct> people = new Span<PersonRefStruct>();
        
        /*
         *
         * You're trying to create an array of ref struct (new PersonRefStruct[10])
            Arrays always live on the heap
            
            Span<PersonRefStruct> people = new Span<PersonRefStruct>(new PersonRefStruct[10]);
         */
        
    }
}

public struct PersonStruct
{
    public string Name { get; set; }
}


/*
 *
 * Feature	Description
Stack-only	                        Cannot be allocated on the heap
Cannot be boxed     	            So can't be used as object or interface
Cannot be a field in a class	    Because classes are heap-allocated
Cannot be captured in lambdas	    To prevent unsafe lifetimes
Cannot be used in async methods	    Because local variables may escape the stack
Cannot be used in iterator methods	Same reason — unsafe lifetime risk
 */
public ref struct PersonRefStruct
{
    public int Age { get; set; }
    public string Name  { get; set; }
}

public ref struct CustomRef
{
    public bool IsValid;
    public Span<int> Inputs;
    public Span<int> Outputs;
}

ref struct School
{
    // array of ref struct not allowed: public Person[] Students { get; set; }
    private PersonRefStruct Director = new();
    

    public School()
    {
    }
}

class SchoolClass
{
    // array of ref struct not allowed: public Person[] Students { get; set; }
    //private Person Director = new(); // class can't contain a ref struct

    public SchoolClass()
    {
    }

    // span lives in stack: error ---> public Span<PersonStruct> personStructs { get; set; }
}