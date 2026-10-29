namespace Event.Distributed.IntegrationTests;

public enum OwnedBffPeerProtection
{
    Shared,
    IndependentValidAuthority,
    WrongPartition,
    GenerationBelowFloor,
    ExpiredAdmission,
    RevokedActiveKey
}
