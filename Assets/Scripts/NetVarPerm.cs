using MLAPI.NetworkedVar;

public class NetVarPerm
{
    public static readonly NetworkedVarSettings Owner2Everyone = new NetworkedVarSettings
    {
        WritePermission = NetworkedVarPermission.OwnerOnly,
        ReadPermission = NetworkedVarPermission.Everyone
    };
    
    public static readonly NetworkedVarSettings Server2Everyone = new NetworkedVarSettings
    {
        WritePermission = NetworkedVarPermission.ServerOnly,
        ReadPermission = NetworkedVarPermission.Everyone
    };
}