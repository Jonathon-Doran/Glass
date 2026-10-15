namespace Glass.Core.Signals;

///////////////////////////////////////////////////////////////////////////////////////////////
// SignalInferenceStatus
//
// Raised by an identification routine when it has something to tell the operator about
// why it did or did not produce a result, such as a prerequisite that is not yet met.
//
// Published on the thread the identification scan runs on.  Subscribers that need a
// specific thread (UI, for example) marshal in their handler.
//
// Carries one line of text intended for display.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SignalInferenceStatus
{
    public string Text
    {
        get;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SignalInferenceStatus (constructor)
    //
    // text:  The line to display.  Must not be null or empty; a routine with nothing to
    //        say does not publish.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SignalInferenceStatus(string text)
    {
        Text = text;
    }
}