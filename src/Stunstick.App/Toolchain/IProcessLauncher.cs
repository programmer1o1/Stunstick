namespace Stunstick.App.Toolchain;

public interface IProcessLauncher
{
	Task<int> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken);
}

