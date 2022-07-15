using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuKeeper.Abstractions.Git;
using NuKeeper.Abstractions.Inspections.Files;
using NuKeeper.Abstractions.Logging;
using NuKeeper.Update.ProcessRunner;

namespace NuKeeper.Git
{
    public class GitCmdDriver : IGitDriver
    {
        private GitUsernamePasswordCredentials _gitCredentials;
        private string _pathGit;
        private INuKeeperLogger _logger;

        public GitCmdDriver(string pathToGit, INuKeeperLogger logger,
            IFolder workingFolder, GitUsernamePasswordCredentials credentials)
        {
            if (string.IsNullOrWhiteSpace(pathToGit))
            {
                throw new ArgumentNullException(nameof(pathToGit));
            }

            if (Path.GetFileNameWithoutExtension(pathToGit) != "git")
            {
                throw new InvalidOperationException($"Invalid path '{pathToGit}'. Path must point to 'git' cmd");
            }

            _pathGit = pathToGit;
            _logger = logger;
            WorkingFolder = workingFolder ?? throw new ArgumentNullException(nameof(workingFolder));
            _gitCredentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        public IFolder WorkingFolder { get; }

        public async Task AddRemote(string name, Uri endpoint)
        {
            if (endpoint != null)
            {
                var uri = CreateCredentialsUri(endpoint, _gitCredentials);

                await StartGitProcess($"remote add {name} {uri.AbsoluteUri}", true);
            }
        }

        private static string GeneratePatString(string token)
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{string.Empty}:{token}"));
            var header = $"-c http.extraHeader=\"Authorization: Basic {encoded}\"";

            return header;
        }

        public async Task Checkout(string branchName)
        {
            await StartGitProcess($"checkout {branchName}", false);
        }

        public async Task CheckoutRemoteToLocal(string branchName)
        {
            await StartGitProcess($"checkout -b {branchName} origin/{branchName}", false);
        }

        public async Task<bool> CheckoutNewBranch(string branchName)
        {
            try
            {
                await StartGitProcess($"checkout -b {branchName}", true);
                return true;
            } catch
            {
                return false;
            }
        }

        public async Task Clone(Uri pullEndpoint)
        {
            await Clone(pullEndpoint, null);
        }

        public async Task Clone(Uri pullEndpoint, string branchName)
        {
            if (pullEndpoint != null)
            {
                
                
                var branchparam = branchName == null ? "" : $" -b {branchName}";
                var args = $"-c http.sslVerify=false {GeneratePatString(_gitCredentials.Password)} clone{branchparam} \"{pullEndpoint.AbsoluteUri}\" .";

                _logger.Normal(
                    $"Git {args}, branch {branchName ?? "default"}, to {WorkingFolder.FullPath}");

                await StartGitProcess(args,
                    true); // Clone into current folder
                _logger.Detailed("Git clone complete");
            }
        }

        public async Task Commit(string message)
        {
            _logger.Detailed($"Git commit with message '{message}'");
            await StartGitProcess($"commit -a -m \"{message}\"", true);
        }

        public async Task<string> GetCurrentHead()
        {
            var getBranchHead = await StartGitProcess($"symbolic-ref -q --short HEAD", true);
            return string.IsNullOrEmpty(getBranchHead) ?
                await StartGitProcess($"rev-parse HEAD", true) :
                getBranchHead;
        }

        public async Task Push(string remoteName, string branchName)
        {
            _logger.Detailed($"Git push to {remoteName}/{branchName}");
            await StartGitProcess($"-c http.sslVerify=false {GeneratePatString(_gitCredentials.Password)} push \"{remoteName}\" {branchName}", true);
        }

        private  async Task<string> StartGitProcess(string arguments, bool ensureSuccess)
        {
            var process = new ExternalProcess(_logger);
            var output = await process.Run(WorkingFolder.FullPath, _pathGit, arguments, ensureSuccess);
            return output.Output.TrimEnd(Environment.NewLine.ToCharArray());
        }

        private Uri CreateCredentialsUri(Uri pullEndpoint, GitUsernamePasswordCredentials gitCredentials)
        {
            if (_gitCredentials?.Username == null)
            {
                return pullEndpoint;
            }

            _logger.Detailed(gitCredentials.Username);

            return pullEndpoint;
            //return new UriBuilder(pullEndpoint)
            //    { UserName = Uri.EscapeDataString(gitCredentials.Username), Password = gitCredentials.Password }.Uri;
        }

        public async Task<IReadOnlyCollection<string>> GetNewCommitMessages(string baseBranchName, string headBranchName)
        {
            var commitlog = await StartGitProcess($"log --oneline --no-decorate --right-only {baseBranchName}...{headBranchName}", true);
            var commitMsgWithId = commitlog
                .Split(Environment.NewLine.ToCharArray())
                .Select(m=>m.Trim())
                .Where(m => !String.IsNullOrWhiteSpace(m));
            var commitMessages = commitMsgWithId
                .Select(m => string.Join(" ", m.Split(' ').Skip(1)))
                .Select(m => m.Trim())
                .Where(m => !String.IsNullOrWhiteSpace(m)).ToList();

            return commitMessages.AsReadOnly();
        }
    }
}
