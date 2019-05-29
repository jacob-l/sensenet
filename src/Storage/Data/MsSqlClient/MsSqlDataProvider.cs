﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SenseNet.Common.Storage.Data.MsSqlClient;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
// ReSharper disable All

// ReSharper disable once CheckNamespace
namespace SenseNet.ContentRepository.Storage.Data.MsSqlClient
{
    public partial class MsSqlDataProvider : DataProvider2
    {
        public override DateTime DateTimeMinValue { get; } = new DateTime(1753, 1, 1, 12, 0, 0);

        public override Task InsertNodeAsync(NodeHeadData nodeHeadData, VersionData versionData, DynamicPropertyData dynamicData,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task UpdateNodeAsync(NodeHeadData nodeHeadData, VersionData versionData, DynamicPropertyData dynamicData,
            IEnumerable<int> versionIdsToDelete, string originalPath = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task CopyAndUpdateNodeAsync(NodeHeadData nodeHeadData, VersionData versionData, DynamicPropertyData dynamicData,
            IEnumerable<int> versionIdsToDelete, int expectedVersionId = 0, string originalPath = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task UpdateNodeHeadAsync(NodeHeadData nodeHeadData, IEnumerable<int> versionIdsToDelete,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task<IEnumerable<NodeData>> LoadNodesAsync(int[] versionIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            var ids = string.Join(",", versionIds.Select(x => x.ToString()));
            return await MsSqlProcedure.ExecuteReaderAsync(SqlScripts.LoadNodes, cmd =>
            {
                cmd.Parameters.Add("@VersionIds", SqlDbType.VarChar, int.MaxValue).Value = ids;
                cmd.Parameters.Add("@LongTextMaxSize", SqlDbType.Int).Value = DataStore.TextAlternationSizeLimit;
            }, reader =>
            {
                var result = new Dictionary<int, NodeData>();

                // Base data
                while (reader.Read())
                {
                    var versionId = reader.GetInt32("VersionId");
                    var nodeTypeId = reader.GetInt32("NodeTypeId");
                    var contentListTypeId = reader.GetSafeInt32("ContentListTypeId");

                    var nodeData = new NodeData(nodeTypeId, contentListTypeId)
                    {
                        Id = reader.GetInt32("NodeId"),
                        VersionId = versionId,
                        Version = new VersionNumber(reader.GetInt16("MajorNumber"), reader.GetInt16("MinorNumber"), (VersionStatus)reader.GetInt16("Status")),
                        ContentListId = reader.GetSafeInt32("ContentListId"),
                        CreatingInProgress = reader.GetSafeBooleanFromByte("CreatingInProgress"),
                        IsDeleted = reader.GetSafeBooleanFromByte("IsDeleted"),
                        // not used: IsInherited
                        ParentId = reader.GetSafeInt32("ParentNodeId"),
                        Name = reader.GetString("Name"),
                        DisplayName = reader.GetSafeString("DisplayName"),
                        Path = reader.GetString("Path"),
                        Index = reader.GetInt32("Index"),
                        Locked = reader.GetSafeBooleanFromByte("Locked"),
                        LockedById = reader.GetSafeInt32("LockedById"),
                        ETag = reader.GetString("ETag"),
                        LockType = reader.GetInt32("LockType"),
                        LockTimeout = reader.GetInt32("LockTimeout"),
                        LockDate = reader.GetDateTimeUtc("LockDate"),
                        LockToken = reader.GetString("LockToken"),
                        LastLockUpdate = reader.GetDateTimeUtc("LastLockUpdate"), 
                        CreationDate = reader.GetDateTimeUtc("NodeCreationDate"),
                        CreatedById = reader.GetInt32("NodeCreatedById"),
                        ModificationDate = reader.GetDateTimeUtc("NodeModificationDate"),
                        ModifiedById = reader.GetInt32("NodeModifiedById"),
                        IsSystem = reader.GetSafeBooleanFromByte("IsSystem"),
                        OwnerId = reader.GetSafeInt32("OwnerId"),
                        SavingState = reader.GetSavingState("SavingState"),
                        ChangedData = reader.GetChangedData("ChangedData"),
                        NodeTimestamp = reader.GetSafeLongFromBytes("NodeTimestamp"),
                        VersionCreationDate = reader.GetDateTimeUtc("CreationDate"),
                        VersionCreatedById = reader.GetInt32("CreatedById"),
                        VersionModificationDate = reader.GetDateTimeUtc("ModificationDate"),
                        VersionModifiedById = reader.GetInt32("ModifiedById"),
                        VersionTimestamp = reader.GetSafeLongFromBytes("VersionTimestamp"),
                    };

                    IDictionary<string, object> dynamicProperties;
                    var serializer = JsonSerializer.Create(SerializerSettings);
                    var dynamicPropertySource = reader.GetSafeString("DynamicProperties");
                    if (dynamicPropertySource != null)
                    {
                        using (var jsonReader = new JsonTextReader(new StringReader(dynamicPropertySource)))
                            dynamicProperties = serializer.Deserialize<IDictionary<string, object>>(jsonReader);
                        foreach (var item in dynamicProperties)
                            nodeData.SetDynamicRawData(ActiveSchema.PropertyTypes[item.Key], item.Value);
                    }

                    result.Add(versionId, nodeData);
                }

                // BinaryProperties
                reader.NextResult();
                while (reader.Read())
                {
                    var versionId = reader.GetInt32(reader.GetOrdinal("VersionId"));
                    var propertyTypeId = reader.GetInt32(reader.GetOrdinal("PropertyTypeId"));

                    var value = GetBinaryDataValueFromReader(reader);

                    var nodeData = result[versionId];
                    nodeData.SetDynamicRawData(propertyTypeId, value);
                }

                //// ReferenceProperties
                //reader.NextResult();
                //while (reader.Read())
                //{
                //    var versionId = reader.GetInt32(reader.GetOrdinal("VersionId"));
                //}

                // LongTextProperties
                reader.NextResult();
                while (reader.Read())
                {
                    var versionId = reader.GetInt32(reader.GetOrdinal("VersionId"));
                    var propertyTypeId = reader.GetInt32("PropertyTypeId");
                    var value = reader.GetSafeString("Value");

                    var nodeData = result[versionId];
                    nodeData.SetDynamicRawData(propertyTypeId, value);
                }

                return result.Values;
            });
        }

        private BinaryDataValue GetBinaryDataValueFromReader(SqlDataReader reader)
        {
            //--BinaryProperties
            /*
                B.BinaryPropertyId,
                B.VersionId,
                B.PropertyTypeId,
                F.FileId,
                F.ContentType,
                F.FileNameWithoutExtension,
                F.Extension,
                F.[Size],
                F.[BlobProvider],
                F.[BlobProviderData],
                F.[Checksum],
                NULL AS Stream, 0 AS Loaded,
                F.[Timestamp]
            */

            return new BinaryDataValue
            {
                Id = reader.GetInt32("BinaryPropertyId"),
                FileId = reader.GetInt32("FileId"),
                ContentType = reader.GetSafeString("ContentType"),
                FileName = new BinaryFileName(
                    reader.GetSafeString("FileNameWithoutExtension") ?? "",
                    reader.GetSafeString("Extension") ?? ""),
                Size = reader.GetInt64("Size"),
                Checksum = reader.GetSafeString("Checksum"),
                BlobProviderName = reader.GetSafeString("BlobProvider"),
                BlobProviderData = reader.GetSafeString("BlobProviderData"),
                Timestamp = reader.GetSafeLongFromBytes("Timestamp"),
                Stream = null
            };
        }

        public override Task DeleteNodeAsync(NodeHeadData nodeHeadData, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task MoveNodeAsync(NodeHeadData sourceNodeHeadData, int targetNodeId, long targetTimestamp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<Dictionary<int, string>> LoadTextPropertyValuesAsync(int versionId, int[] notLoadedPropertyTypeIds,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<BinaryDataValue> LoadBinaryPropertyValueAsync(int versionId, int propertyTypeId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(BlobStorage.LoadBinaryProperty(versionId, propertyTypeId));
        }

        public override Task<bool> NodeExistsAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task<NodeHead> LoadNodeHeadAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sql = SqlScripts.LoadNodeHead("LoadNodeHead by Path", where: "Node.Path = @Path COLLATE Latin1_General_CI_AS");

            return await MsSqlProcedure.ExecuteReaderAsync(sql, cmd =>
            {
                cmd.Parameters.Add("@Path", SqlDbType.NVarChar, PathMaxLength).Value = path;
            }, reader =>
            {
                if (!reader.Read())
                    return null;
                return GetNodeHeadFromReader(reader);
            });
        }

        public override async Task<NodeHead> LoadNodeHeadAsync(int nodeId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sql = SqlScripts.LoadNodeHead("LoadNodeHead by NodeId", where: "Node.NodeId = @NodeId");

            return await MsSqlProcedure.ExecuteReaderAsync(sql, cmd =>
            {
                cmd.Parameters.Add("@NodeId", SqlDbType.Int).Value = nodeId;
            }, reader =>
            {
                if (!reader.Read())
                    return null;
                return GetNodeHeadFromReader(reader);
            });
        }

        public override Task<NodeHead> LoadNodeHeadByVersionIdAsync(int versionId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sql = SqlScripts.LoadNodeHead(
                trace: "LoadNodeHead by VersionId",
                join: "JOIN Versions V ON V.NodeId = Node.NodeId",
                where: "V.VersionId = @VersionId");
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        private NodeHead GetNodeHeadFromReader(SqlDataReader reader)
        {
            return new NodeHead(
                reader.GetInt32(0),         // nodeId,
                reader.GetString(1),        // name,
                reader.GetSafeString(2),    // displayName,
                reader.GetString(3),        // pathInDb,
                reader.GetSafeInt32(4),     // parentNodeId,
                reader.GetInt32(5),         // nodeTypeId,
                reader.GetSafeInt32(6),     // contentListTypeId,
                reader.GetSafeInt32(7),     // contentListId,
                reader.GetDateTimeUtc(8),   // creationDate,
                reader.GetDateTimeUtc(9),   // modificationDate,
                reader.GetSafeInt32(10),    // lastMinorVersionId,
                reader.GetSafeInt32(11),    // lastMajorVersionId,
                reader.GetSafeInt32(12),    // ownerId,
                reader.GetSafeInt32(13),    // creatorId,
                reader.GetSafeInt32(14),    // modifierId,
                reader.GetSafeInt32(15),    // index,
                reader.GetSafeInt32(16),    // lockerId
                GetLongFromBytes((byte[])reader.GetValue(17))     // timestamp
            );
        }

        public override async Task<IEnumerable<NodeHead>> LoadNodeHeadsAsync(IEnumerable<int> nodeIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sql = SqlScripts.LoadNodeHead(
                trace: "LoadNodeHead by NodeId set",
                scriptHead: @"DECLARE @NodeIdTable AS TABLE(Id INT) INSERT INTO @NodeIdTable SELECT CONVERT(int, [value]) FROM STRING_SPLIT(@NodeIds, ',');",
                where: "Node.NodeId IN (SELECT Id FROM @NodeIdTable)");

            var ids = string.Join(",", nodeIds.Select(x => x.ToString()));
            return await MsSqlProcedure.ExecuteReaderAsync(sql, cmd =>
            {
                cmd.Parameters.Add("@NodeIds", SqlDbType.VarChar, int.MaxValue).Value = ids;
            }, reader =>
            {
                var result = new List<NodeHead>();

                while (reader.Read())
                    result.Add(GetNodeHeadFromReader(reader));

                return result;
            });
        }

        public override Task<NodeHead.NodeVersion[]> GetNodeVersions(int nodeId, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<VersionNumber>> GetVersionNumbersAsync(int nodeId, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<VersionNumber>> GetVersionNumbersAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<int> InstanceCountAsync(int[] nodeTypeIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<int>> GetChildrenIdentfiersAsync(int parentId, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task<IEnumerable<int>> QueryNodesByTypeAndPathAndNameAsync(int[] nodeTypeIds, string[] pathStart, bool orderByPath, string name,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var sql = new StringBuilder("SELECT NodeId FROM Nodes WHERE ");
            var first = true;

            if (pathStart != null && pathStart.Length > 0)
            {
                for (int i = 0; i < pathStart.Length; i++)
                    if (pathStart[i] != null)
                        pathStart[i] = pathStart[i].Replace("'", "''");

                sql.AppendLine("(");
                for (int i = 0; i < pathStart.Length; i++)
                {
                    if (i > 0)
                        sql.AppendLine().Append(" OR ");
                    sql.Append(" Path LIKE N'");
                    sql.Append(EscapeForLikeOperator(pathStart[i]));
                    if (!pathStart[i].EndsWith(RepositoryPath.PathSeparator))
                        sql.Append(RepositoryPath.PathSeparator);
                    sql.Append("%' COLLATE Latin1_General_CI_AS");
                }
                sql.AppendLine(")");
                first = false;
            }

            if (name != null)
            {
                name = name.Replace("'", "''");
                if (!first)
                    sql.Append(" AND");
                sql.Append(" Name = '").Append(name).Append("'");
                first = false;
            }

            if (nodeTypeIds != null)
            {
                if (!first)
                    sql.Append(" AND");
                sql.Append(" NodeTypeId");
                if (nodeTypeIds.Length == 1)
                    sql.Append(" = ").Append(nodeTypeIds[0]);
                else
                    sql.Append(" IN (").Append(string.Join(", ", nodeTypeIds)).Append(")");
            }

            if (orderByPath)
                sql.AppendLine().Append("ORDER BY Path");

            return await MsSqlProcedure.ExecuteReaderAsync(sql.ToString(), reader =>
            {
                var result = new List<int>();
                while (reader.Read())
                    result.Add(reader.GetSafeInt32(0));
                return (IEnumerable<int>) result;
            });
        }

        public override Task<IEnumerable<int>> QueryNodesByTypeAndPathAndPropertyAsync(int[] nodeTypeIds, string pathStart, bool orderByPath, List<QueryPropertyData> properties,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<int>> QueryNodesByReferenceAndTypeAsync(string referenceName, int referredNodeId, int[] nodeTypeIds,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<NodeType>> LoadChildTypesToAllowAsync(int nodeId, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<List<ContentListType>> GetContentListTypesInTreeAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<int> AcquireTreeLockAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<bool> IsTreeLockedAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task ReleaseTreeLockAsync(int[] lockIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<Dictionary<int, string>> LoadAllTreeLocksAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task SaveIndexDocumentAsync(NodeData nodeData, IndexDocument indexDoc,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task SaveIndexDocumentAsync(int versionId, IndexDocument indexDoc,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<IndexDocumentData>> LoadIndexDocumentsAsync(IEnumerable<int> versionIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<IndexDocumentData>> LoadIndexDocumentsAsync(string path, int[] excludedNodeTypes,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IEnumerable<int>> LoadNotIndexedNodeIdsAsync(int fromId, int toId, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task<int> GetLastIndexingActivityIdAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return await MsSqlProcedure.ExecuteScalarAsync(
                SqlScripts.GetLastIndexingActivityId, value => value == DBNull.Value ? 0 : Convert.ToInt32(value));
        }

        public override Task<IIndexingActivity[]> LoadIndexingActivitiesAsync(int fromId, int toId, int count, bool executingUnprocessedActivities,
            IIndexingActivityFactory activityFactory, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<IIndexingActivity[]> LoadIndexingActivitiesAsync(int[] gaps, bool executingUnprocessedActivities,
            IIndexingActivityFactory activityFactory, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<ExecutableIndexingActivitiesResult> LoadExecutableIndexingActivitiesAsync(IIndexingActivityFactory activityFactory, int maxCount,
            int runningTimeoutInSeconds, int[] waitingActivityIds,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task RegisterIndexingActivityAsync(IIndexingActivity activity,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task UpdateIndexingActivityRunningStateAsync(int indexingActivityId, IndexingActivityRunningState runningState,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task RefreshIndexingActivityLockTimeAsync(int[] waitingIds,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task DeleteFinishedIndexingActivitiesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task DeleteAllIndexingActivitiesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task<RepositorySchemaData> LoadSchemaAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return await MsSqlProcedure.ExecuteReaderAsync(SqlScripts.LoadSchema, reader =>
            {
                var schema = new RepositorySchemaData();

                if (reader.Read())
                    schema.Timestamp = reader.GetSafeLongFromBytes("Timestamp");

                // PropertyTypes
                reader.NextResult();
                var propertyTypes = new List<PropertyTypeData>();
                schema.PropertyTypes = propertyTypes;
                while (reader.Read())
                {
                    propertyTypes.Add(new PropertyTypeData
                    {
                        Id = reader.GetInt32("PropertyTypeId"),
                        Name = reader.GetString("Name"),
                        DataType = reader.GetEnumValueByName<DataType>("DataType"),
                        Mapping = reader.GetInt32("Mapping"),
                        IsContentListProperty = reader.GetSafeBooleanFromByte("IsContentListProperty")
                    });
                }

                // NodeTypes
                reader.NextResult();
                var nodeTypes = new List<NodeTypeData>();
                schema.NodeTypes = nodeTypes;
                var tree = new List<(NodeTypeData Data, int ParentId)>(); // data, parentId
                while (reader.Read())
                {
                    var data = new NodeTypeData
                    {
                        Id = reader.GetInt32("NodeTypeId"),
                        Name = reader.GetString("Name"),
                        ClassName = reader.GetString("ClassName"),
                        Properties = new List<string>(
                            reader.GetSafeString("Properties")?.Split(new []{' '}, StringSplitOptions.RemoveEmptyEntries) ?? new string[0])
                    };
                    var parentId = reader.GetSafeInt32("ParentId");
                    tree.Add((data, parentId));
                    nodeTypes.Add(data);
                }
                foreach (var item in tree)
                {
                    var parent = tree.FirstOrDefault(x => x.Data.Id == item.ParentId);
                    item.Data.ParentName = parent.Data?.Name;
                }

                // ContentListTypes
                var contentListTypes = new List<ContentListTypeData>();
                schema.ContentListTypes = contentListTypes;
                //UNDONE:DB: Load ContentListTypes
                //reader.NextResult();
                //while (reader.Read())
                //{
                //}

                return schema;
            });
        }

        public override SchemaWriter CreateSchemaWriter()
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<string> StartSchemaUpdateAsync(long schemaTimestamp, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<long> FinishSchemaUpdateAsync(string schemaLock, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task WriteAuditEventAsync(AuditEventInfo auditEvent, CancellationToken cancellationToken = default(CancellationToken))
        {
            var unused = await MsSqlProcedure.ExecuteScalarAsync(SqlScripts.WriteAuditEvent, cmd =>
            {
                cmd.Parameters.Add("@EventID", SqlDbType.Int).Value = auditEvent.EventId;
                cmd.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = (object)auditEvent.Category ?? DBNull.Value;
                cmd.Parameters.Add("@Priority", SqlDbType.Int).Value = auditEvent.Priority;
                cmd.Parameters.Add("@Severity", SqlDbType.VarChar, 30).Value = auditEvent.Severity;
                cmd.Parameters.Add("@Title", SqlDbType.NVarChar, 256).Value = (object)auditEvent.Title ?? DBNull.Value;
                cmd.Parameters.Add("@ContentId", SqlDbType.Int).Value = auditEvent.ContentId;
                cmd.Parameters.Add("@ContentPath", SqlDbType.NVarChar, PathMaxLength).Value = (object)auditEvent.ContentPath ?? DBNull.Value;
                cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, 450).Value = (object)auditEvent.UserName ?? DBNull.Value;
                cmd.Parameters.Add("@LogDate", SqlDbType.DateTime).Value = auditEvent.Timestamp;
                cmd.Parameters.Add("@MachineName", SqlDbType.VarChar, 32).Value = (object)auditEvent.MachineName ?? DBNull.Value;
                cmd.Parameters.Add("@AppDomainName", SqlDbType.VarChar, 512).Value = (object)auditEvent.AppDomainName ?? DBNull.Value;
                cmd.Parameters.Add("@ProcessID", SqlDbType.VarChar, 256).Value = auditEvent.ProcessId;
                cmd.Parameters.Add("@ProcessName", SqlDbType.VarChar, 512).Value = (object)auditEvent.ProcessName ?? DBNull.Value;
                cmd.Parameters.Add("@ThreadName", SqlDbType.VarChar, 512).Value = (object)auditEvent.ThreadName ?? DBNull.Value;
                cmd.Parameters.Add("@Win32ThreadId", SqlDbType.VarChar, 128).Value = auditEvent.ThreadId;
                cmd.Parameters.Add("@Message", SqlDbType.VarChar, 1500).Value = (object)auditEvent.Message ?? DBNull.Value;
                cmd.Parameters.Add("@Formattedmessage", SqlDbType.NText).Value = (object)auditEvent.FormattedMessage ?? DBNull.Value;
            },
            value => value == DBNull.Value ? 0 : Convert.ToInt32(value));
        }

        public override DateTime RoundDateTime(DateTime d)
        {
            return new DateTime(d.Ticks / 100000 * 100000);
        }

        public override bool IsCacheableText(string text)
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<string> GetNameOfLastNodeWithNameBaseAsync(int parentId, string namebase, string extension,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task<long> GetTreeSizeAsync(string path, bool includeChildren,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RepositoryPath.CheckValidPath(path);
            return await MsSqlProcedure.ExecuteScalarAsync(SqlScripts.GetTreeSize, cmd =>
            {
                cmd.Parameters.Add("@IncludeChildren", SqlDbType.TinyInt).Value = includeChildren ? (byte)1 : 0;
                cmd.Parameters.Add("@NodePath", SqlDbType.NVarChar, PathMaxLength).Value = path;
            },
            value => (long) value
            );
        }

        public override Task<int> GetNodeCountAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override Task<int> GetVersionCountAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException(new StackTrace().GetFrame(0).GetMethod().Name); //UNDONE:DB@ NotImplementedException
        }

        public override async Task InstallInitialDataAsync(InitialData data, CancellationToken cancellationToken = default(CancellationToken))
        {
            await MsSqlDataInstaller.InstallInitialDataAsync(data, this, ConnectionStrings.ConnectionString);
        }

        public override async Task<IEnumerable<EntityTreeNodeData>> LoadEntityTreeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return await MsSqlProcedure.ExecuteReaderAsync(SqlScripts.LoadEntityTree, reader =>
            {
                var result = new List<EntityTreeNodeData>();
                while (reader.Read())
                    result.Add(new EntityTreeNodeData
                    {
                        Id = reader.GetInt32("NodeId"),
                        ParentId = reader.GetSafeInt32("ParentNodeId"),
                        OwnerId = reader.GetSafeInt32("OwnerId")
                    });
                return result;
            });
        }


        /* ======================================================================================================= TOOLS */

        internal static long GetLongFromBytes(byte[] bytes)
        {
            var @long = 0L;
            for (int i = 0; i < bytes.Length; i++)
                @long = (@long << 8) + bytes[i];
            return @long;
        }

        internal static byte[] GetBytesFromLong(long @long)
        {
            var bytes = new byte[8];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[7 - i] = (byte)(@long & 0xFF);
                @long = @long >> 8;
            }
            return bytes;
        }

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings //UNDONE:DB Use a common instance
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.Indented
        };

        private static string EscapeForLikeOperator(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = text.Replace("[", "[[]").Replace("_", "[_]").Replace("%", "[%]");

            return text;
        }
    }
}
