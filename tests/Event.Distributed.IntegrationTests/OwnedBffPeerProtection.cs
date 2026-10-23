namespace Event.Distributed.IntegrationTests;

public enum OwnedBffPeerProtection
{
    Shared,
    WrongPartition,
    GenerationBelowFloor,
    ExpiredAdmission,
    RevokedActiveKey
}
