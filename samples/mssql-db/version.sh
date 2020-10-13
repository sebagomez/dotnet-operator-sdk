sdk=$(xmllint --xpath 'string(//PackageReference[@Include="ContainerSolutions.Kubernetes.OperatorSDK"]/@Version)' ./samples/mssql-db/mssql-db.csproj)
component=$(xmllint --xpath 'string(//Version)' ./samples/mssql-db/mssql-db.csproj)

echo $component-$sdk