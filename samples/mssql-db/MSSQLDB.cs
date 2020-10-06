using ContainerSolutions.OperatorSDK;

namespace mssql_db
{
	public class MSSQLDB : BaseCRD
	{
		public MSSQLDB() :
			base("samples.k8s-cs-controller", "v1", "mssqldbs", "mssqldb")
		{ }

		public MSSQLDBSpec Spec { get; set; }

		public override bool Equals(object obj)
		{
			if (obj == null)
				return false;

			return ToString().Equals(obj.ToString());
		}

		public override int GetHashCode()
		{
			return ToString().GetHashCode();
		}

		public override string ToString()
		{
			return Spec.ToString();
		}

	}
}
