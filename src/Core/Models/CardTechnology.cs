namespace CredBench.Core.Models;

[Flags]
public enum CardTechnology
{
    Unknown = 0,
    PIV = 1,
    DESFire = 2,
    IClass = 4,
    PKOC = 8
}
