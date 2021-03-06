﻿

namespace Jumoo.uSync.BackOffice.Handlers
{
    using System;
    using System.IO;
    using System.Xml.Linq;
    using System.Collections.Generic;

    using Umbraco.Core;
    using Umbraco.Core.Models;
    using Umbraco.Core.Services;
    using Umbraco.Core.Logging;

    using Jumoo.uSync.Core;
    using Jumoo.uSync.BackOffice.Helpers;
    using Core.Extensions;

    public class DataTypeHandler : uSyncBaseHandler<IDataTypeDefinition>, ISyncHandler
    {
        public string Name { get { return "uSync: DataTypeHandler"; } }
        public int Priority { get { return uSyncConstants.Priority.DataTypes; } }
        public string SyncFolder { get { return Constants.Packaging.DataTypeNodeName; } }

        IDataTypeService _dataTypeService;
        public DataTypeHandler()
        {
            _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
        }

        public override SyncAttempt<IDataTypeDefinition> Import(string filePath, bool force = false)
        {
            LogHelper.Debug<IDataTypeDefinition>(">> Import: {0}", () => filePath);

            if (!System.IO.File.Exists(filePath))
                throw new FileNotFoundException(filePath);

            var node = XElement.Load(filePath);

            return uSyncCoreContext.Instance.DataTypeSerializer.Deserialize(node, force, false);
        }

        public override uSyncAction DeleteItem(Guid key, string keyString)
        {
            IDataTypeDefinition item = null;
            if (key != Guid.Empty)
                item = _dataTypeService.GetDataTypeDefinitionById(key);

            /* delete only by key 
            if (item == null && !string.IsNullOrEmpty(keyString))
                item = _dataTypeService.GetDataTypeDefinitionByName(keyString);
            */

            if (item != null)
            {
                LogHelper.Info<DataTypeHandler>("Deleting datatype: {0}", () => item.Name);
                _dataTypeService.Delete(item);
                return uSyncAction.SetAction(true, keyString, typeof(IDataTypeDefinition), ChangeType.Delete, "Removed");
            }

            return uSyncAction.Fail(keyString, typeof(IDataTypeDefinition), ChangeType.Delete, "Not found");
        }

        public IEnumerable<uSyncAction> ExportAll(string folder)
        {
            LogHelper.Info<DataTypeHandler>("Exporting all DataTypes");

            List<uSyncAction> actions = new List<uSyncAction>();

            var _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
            foreach (var item in _dataTypeService.GetAllDataTypeDefinitions())
            {
                if (item != null)
                    actions.Add(ExportToDisk(item, folder));
            }

            return actions;
        }

        public uSyncAction ExportToDisk(IDataTypeDefinition item, string folder)
        {
            if (item == null)
                return uSyncAction.Fail(Path.GetFileName(folder), typeof(IDataTypeDefinition), "item not set");

            try
            {
                var attempt = uSyncCoreContext.Instance.DataTypeSerializer.Serialize(item);
                var filename = string.Empty;

                if (attempt.Success)
                {
                    filename = uSyncIOHelper.SavePath(folder, SyncFolder, item.Name.ToSafeAlias());
                    uSyncIOHelper.SaveNode(attempt.Item, filename);
                }

                return uSyncActionHelper<XElement>.SetAction(attempt, filename);
            }
            catch (Exception ex)
            {
                return uSyncAction.Fail(item.Name, item.GetType(), ChangeType.Export, ex);
            }
        }

        public void RegisterEvents()
        {
            DataTypeService.Saved += DataTypeService_Saved;
            DataTypeService.Deleted += DataTypeService_Deleted;
        }

        private void DataTypeService_Deleted(IDataTypeService sender, Umbraco.Core.Events.DeleteEventArgs<IDataTypeDefinition> e)
        {
            if (uSyncEvents.Paused)
                return;

            foreach (var item in e.DeletedEntities)
            {
                LogHelper.Info<DataTypeHandler>("Delete: Deleting uSync File for item: {0}", () => item.Name);
                uSyncIOHelper.ArchiveRelativeFile(SyncFolder, item.Name.ToSafeAlias());

                uSyncBackOfficeContext.Instance.Tracker.AddAction(SyncActionType.Delete, item.Key, item.Name, typeof(IDataTypeDefinition));
            }
        }

        private void DataTypeService_Saved(IDataTypeService sender, Umbraco.Core.Events.SaveEventArgs<IDataTypeDefinition> e)
        {
            if (uSyncEvents.Paused)
                return;

            foreach (var item in e.SavedEntities)
            {
                LogHelper.Info<DataTypeHandler>("Save: Saving uSync file for item: {0}", () => item.Name);
                var action = ExportToDisk(item, uSyncBackOfficeContext.Instance.Configuration.Settings.Folder);
                if (action.Success)
                {
                    NameChecker.ManageOrphanFiles(SyncFolder, item.Key, action.FileName);
                }
            }
        }

        public override uSyncAction ReportItem(string file)
        {
            LogHelper.Debug<DataTypeHandler>("Report: {0}", () => file);

            var node = XElement.Load(file);

            LogHelper.Debug<DataTypeHandler>("Report: {0}", () => node.ToString());

            var update = uSyncCoreContext.Instance.DataTypeSerializer.IsUpdate(node);
            return uSyncActionHelper<IDataTypeDefinition>.ReportAction(update, node.NameFromNode());
        }

    }
}
