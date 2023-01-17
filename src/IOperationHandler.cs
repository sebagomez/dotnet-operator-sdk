using System.Threading.Tasks;
using k8s;

namespace ContainerSolutions.OperatorSDK
{
	public interface IOperationHandler<T> where T : BaseCRD
	{
		Task OnAdded(IKubernetes k8s, T crd);

		Task OnDeleted(IKubernetes k8s, T crd);

		Task OnUpdated(IKubernetes k8s, T crd);

		Task OnBookmarked(IKubernetes k8s, T crd);

		Task OnError(IKubernetes k8s, T crd);

		Task CheckCurrentState(IKubernetes k8s);
	}
}
