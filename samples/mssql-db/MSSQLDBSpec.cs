namespace mssql_db
{
	public class MSSQLDBSpec
	{
		public string DBName { get; set; }

		public string ConfigMap { get; set; }

		public string Credentials { get; set; }

		public override string ToString()
		{
			return $"{DBName}:{ConfigMap}:{Credentials}"; 
		}
	}
}
