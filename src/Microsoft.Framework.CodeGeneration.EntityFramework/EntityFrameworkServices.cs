﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Data.Entity;
using Microsoft.Framework.Runtime;

namespace Microsoft.Framework.CodeGeneration.EntityFramework
{
    public class EntityFrameworkServices : IEntityFrameworkService
    {
        private readonly IDbContextEditorServices _dbContextEditorServices;
        private readonly IApplicationEnvironment _environment;
        private readonly ILibraryManager _libraryManager;
        private readonly IAssemblyLoaderEngine _loader;
        private readonly IModelTypesLocator _modelTypesLocator;
        private static int _counter = 1;

        public EntityFrameworkServices(
            [NotNull]ILibraryManager libraryManager,
            [NotNull]IApplicationEnvironment environment,
            [NotNull]IAssemblyLoaderEngine loader,
            [NotNull]IModelTypesLocator modelTypesLocator,
            [NotNull]IDbContextEditorServices dbContextEditorServices)
        {
            _libraryManager = libraryManager;
            _environment = environment;
            _loader = loader;
            _modelTypesLocator = modelTypesLocator;
            _dbContextEditorServices = dbContextEditorServices;
        }

        public async Task<ModelMetadata> GetModelMetadata(string dbContextTypeName, ITypeSymbol modelTypeSymbol)
        {
            Type dbContextType;
            var dbContextSymbols = _modelTypesLocator.GetType(dbContextTypeName).ToList();
            var isNewDbContext = false;
            SyntaxTree newDbContextTree = null;
            NewDbContextTemplateModel dbContextTemplateModel = null;

            if (dbContextSymbols.Count == 0)
            {
                isNewDbContext = true;

                dbContextTemplateModel = new NewDbContextTemplateModel(dbContextTypeName, modelTypeSymbol);
                newDbContextTree = await _dbContextEditorServices.AddNewContext(dbContextTemplateModel);

                var projectCompilation = _libraryManager.GetProject(_environment).Compilation;
                var newAssemblyName = projectCompilation.AssemblyName + _counter++;
                var newCompilation = projectCompilation.AddSyntaxTrees(newDbContextTree).WithAssemblyName(newAssemblyName);

                var result = CommonUtilities.GetAssemblyFromCompilation(_loader, newCompilation);
                if (result.Success)
                {
                    dbContextType = result.Assembly.GetType(dbContextTypeName);
                    if (dbContextType == null)
                    {
                        throw new InvalidOperationException("There was an error creating a DbContext, there was no type returned after compiling the new assembly successfully");
                    }
                }
                else
                {
                    throw new InvalidOperationException("There was an error creating a DbContext :" + string.Join("\n", result.ErrorMessages));
                }
            }
            else
            {
                dbContextType = _libraryManager.GetReflectionType(_environment, dbContextTypeName);

                if (dbContextType == null)
                {
                    throw new InvalidOperationException("Could not get the reflection type for DbContext : " + dbContextTypeName);
                }
            }

            var modelTypeName = modelTypeSymbol.FullNameForSymbol();
            var modelType = _libraryManager.GetReflectionType(_environment, modelTypeName);

            if (modelType == null)
            {
                throw new InvalidOperationException("Could not get the reflection type for Model : " + modelTypeName);
            }

            var metadata = GetModelMetadata(dbContextType, modelType);

            // Write the DbContext if getting the model metadata is successful
            if (isNewDbContext)
            {
                await WriteDbContext(dbContextTemplateModel, newDbContextTree);
            }

            return metadata;
        }

        private async Task WriteDbContext(NewDbContextTemplateModel dbContextTemplateModel,
            SyntaxTree newDbContextTree)
        {
            //ToDo: What's the best place to write the DbContext?
            var outputPath = Path.Combine(
                _environment.ApplicationBasePath,
                "Models",
                dbContextTemplateModel.DbContextTypeName + ".cs");

            if (File.Exists(outputPath))
            {
                // Odd case, a file exists with the same name as the DbContextTypeName but perhaps
                // the type defined in that file is different, what should we do in this case?
                // How likely is the above scenario?
                // Perhaps we can enumerate files with prefix and generate a safe name? For now, just throw.
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    "There was an error creating a DbContext, the file {0} already exists",
                    outputPath));
            }

            var sourceText = await newDbContextTree.GetTextAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            using (var fileStream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
            {
                using (var streamWriter = new StreamWriter(stream: fileStream, encoding: Encoding.UTF8))
                {
                    sourceText.Write(streamWriter);
                }
            }
        }

        private ModelMetadata GetModelMetadata([NotNull]Type dbContextType, [NotNull]Type modelType)
        {
            DbContext dbContextInstance;
            try
            {
                dbContextInstance = Activator.CreateInstance(dbContextType) as DbContext;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("There was an error creating the DbContext instance to get the model: " + ex);
            }

            if (dbContextInstance == null)
            {
                throw new InvalidOperationException(string.Format(
                    "Instance of type {0} could not be cast to DbContext",
                    dbContextType.FullName));
            }

            var entityType = dbContextInstance.Model.GetEntityType(modelType);
            if (entityType == null)
            {
                throw new InvalidOperationException(string.Format(
                    "There is no entity type {0} on DbContext {1}",
                    modelType.FullName,
                    dbContextType.FullName));
            }

            return new ModelMetadata(entityType, dbContextType);
        }
    }
}