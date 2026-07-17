using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ProtheusPulse.Service.Hubs;

[Authorize]
public sealed class PulseHub : Hub;
