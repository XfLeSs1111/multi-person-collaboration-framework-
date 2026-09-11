using System.Runtime.CompilerServices;

// EditMode test assemblies need the clock-injectable internals on
// NetworkInteractable (ServerTryAcquire/ServerAdvanceLease overloads).
[assembly: InternalsVisibleTo("Socket.Multiplayer.Tests")]
[assembly: InternalsVisibleTo("Socket.Multiplayer.Tests.Common")]
