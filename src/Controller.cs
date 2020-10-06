using System;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ContainerSolutions.OperatorSDK
{
    public class Controller<T> where T : BaseCRD
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public Kubernetes Kubernetes { get; private set; }

        private readonly IOperationHandler<T> m_handler;
        private readonly T m_crd;
        private Watcher<T> m_watcher;

        static Controller()
        {
            ConfigLogger();
        }

        static bool s_loggerConfiged = false;
        public static void ConfigLogger()
        {
            if (!s_loggerConfiged)
            {
                var config = new LoggingConfiguration();
                var consoleTarget = new ColoredConsoleTarget
                {
                    Name = "coloredConsole",
                    Layout = "${longdate} [${level:uppercase=true}] ${logger}:${message}",
                };
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget, "*");
                LogManager.Configuration = config;

                s_loggerConfiged = true;
            }
        }

        public Controller(T crd, IOperationHandler<T> handler)
        {
            KubernetesClientConfiguration config;
            if (KubernetesClientConfiguration.IsInCluster())
            {
                config = KubernetesClientConfiguration.InClusterConfig();
            }
            else
            {
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
            }

            Kubernetes = new Kubernetes(config);
            m_crd = crd;
            m_handler = handler;
        }

        ~Controller()
        {
            DisposeWatcher();
        }

        public async Task SatrtAsync(string k8sNamespace = "")
        {
            var listResponse = await Kubernetes.ListNamespacedCustomObjectWithHttpMessagesAsync(m_crd.Group, m_crd.Version, k8sNamespace, m_crd.Plural, watch: true);

            m_watcher = listResponse.Watch<T, object>(this.OnTChange, this.OnError);

            await ReconciliationLoop();
        }
        private Task ReconciliationLoop()
        {
            return Task.Run(() =>
            {
				Log.Info($"Reconciliation Loop for CRD {m_crd.Singular} will run every {m_crd.ReconciliationCheckInterval} seconds.");

                while (true)
                {
					Thread.Sleep(m_crd.ReconciliationCheckInterval * 1000);

                    m_handler.CheckCurrentState(Kubernetes);
                }
            });
        }

        void DisposeWatcher()
        {
            if (m_watcher != null && m_watcher.Watching)
                m_watcher.Dispose();
        }

        private async void OnTChange(WatchEventType type, T item)
        {
            Log.Info($"{typeof(T)} {item.Name()} {type} on Namespace {item.Namespace()}");
            switch (type)
            {
                case WatchEventType.Added:
                    if (m_handler != null)
                        await m_handler.OnAdded(Kubernetes, item);
                    return;
                case WatchEventType.Modified:
                    if (m_handler != null)
                        await m_handler.OnUpdated(Kubernetes, item);
                    return;
                case WatchEventType.Deleted:
                    if (m_handler != null)
                        await m_handler.OnDeleted(Kubernetes, item);
                    return;
                case WatchEventType.Bookmark:
                    if (m_handler != null)
                        await m_handler.OnBookmarked(Kubernetes, item);
                    return;
                case WatchEventType.Error:
                    if (m_handler != null)
                        await m_handler.OnError(Kubernetes, item);
                    return;
                default:
                    Log.Warn($"Don't know what to do with {type}");
                    break;
            };
        }

        private void OnError(Exception exception)
        {
            Log.Fatal(exception);
        }
    }
}
