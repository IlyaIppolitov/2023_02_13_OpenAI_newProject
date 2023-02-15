using System.Collections.Concurrent;

internal enum State
{
    None
}


internal class UserState
{
    //public State State { get; set; }

    public ConcurrentQueue<DateTime> dateTimes { get; set; }

    public UserState() 
    {
        dateTimes = new ConcurrentQueue<DateTime>();
    }
}