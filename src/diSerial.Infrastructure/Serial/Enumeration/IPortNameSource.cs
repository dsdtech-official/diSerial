namespace DiSerial.Infrastructure.Serial.Enumeration;

internal interface IPortNameSource
{
    // List serial port names.
    string[] GetPortNames();
}

// Read runtime serial port names.
internal sealed class BclPortNameSource : IPortNameSource
{
    // List serial port names.
    public string[] GetPortNames() => System.IO.Ports.SerialPort.GetPortNames();
}
