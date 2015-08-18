﻿//-----------------------------------------------------------------------
// <copyright file="AnalysisConfigGeneratorTests.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------


using Microsoft.VisualStudio.TestTools.UnitTesting;
using SonarQube.Common;
using SonarQube.TeamBuild.Integration;
using System.Collections.Generic;
using System.IO;
using TestUtilities;

namespace SonarQube.TeamBuild.PreProcessor.Tests
{
    [TestClass]
    public class AnalysisConfigGeneratorTests
    {
        public TestContext TestContext { get; set; }

        #region Tests

        [TestMethod]
        public void AnalysisConfGen_CmdLinePropertiesOverrideFileSettings()
        {
            // Checks command line properties override those fetched from the server

            // Arrange
            string analysisDir = TestUtils.CreateTestSpecificFolder(this.TestContext);

            TestLogger logger = new TestLogger();

            // The set of server properties to return.
            // Server settings are stored separately from user-supplied settings so
            // all of them should appear in the file
            Dictionary<string, string> serverSettings = new Dictionary<string, string>();
            serverSettings.Add("shared.key1", "server value 1");
            serverSettings.Add("server.only", "server value 3");
            serverSettings.Add("xxx", "server value xxx - lower case");

            // The set of command line properties to supply.
            // The command line settings should override the file settings
            ListPropertiesProvider cmdLineProperties = new ListPropertiesProvider();
            cmdLineProperties.AddProperty("shared.key1", "cmd line value1 - should override file");
            cmdLineProperties.AddProperty("cmd.line.only", "cmd line value4 - only in file");
            cmdLineProperties.AddProperty("XXX", "cmd line value XXX");
            cmdLineProperties.AddProperty(SonarProperties.HostUrl, "http://host");

            // The set of file properties to supply
            ListPropertiesProvider fileProperties = new ListPropertiesProvider();
            fileProperties.AddProperty("shared.key1", "file value1 - should be overridden by the cmd line");
            fileProperties.AddProperty("file.only", "file value3 - only in file");
            fileProperties.AddProperty("XXX", "file value XXX - upper case");

            ProcessedArgs args = new ProcessedArgs("key", "name", "version", false, cmdLineProperties, fileProperties);

            TeamBuildSettings settings = TeamBuildSettings.CreateNonTeamBuildSettings(analysisDir);
            Directory.CreateDirectory(settings.SonarConfigDirectory); // config directory needs to exist


            // Act
            AnalysisConfig actualConfig =  AnalysisConfigGenerator.GenerateFile(args, settings, serverSettings, logger);

            // Assert
            AssertConfigFileExists(actualConfig);
            logger.AssertErrorsLogged(0);
            logger.AssertWarningsLogged(0);

            // Server
            AssertExpectedServerSetting("shared.key1", "server value 1", actualConfig);
            AssertExpectedServerSetting("server.only", "server value 3", actualConfig);
            AssertExpectedServerSetting("xxx", "server value xxx - lower case", actualConfig);

            // Cmd-line
            AssertExpectedLocalSetting("shared.key1", "cmd line value1 - should override file", actualConfig);
            AssertExpectedLocalSetting("cmd.line.only", "cmd line value4 - only in file", actualConfig);
            AssertExpectedLocalSetting("XXX", "cmd line value XXX", actualConfig);
            AssertExpectedLocalSetting(SonarProperties.HostUrl, "http://host", actualConfig);

            // Non-overridden file values
            AssertExpectedLocalSetting("file.only", "file value3 - only in file", actualConfig);
        }

        [TestMethod]
        [WorkItem(127)] // Do not store the db and server credentials in the config files: http://jira.sonarsource.com/browse/SONARMSBRU-127
        public void AnalysisConfGen_AnalysisConfigDoesNotContainSensitiveData()
        {
            // Arrange
            string analysisDir = TestUtils.CreateTestSpecificFolder(this.TestContext);

            TestLogger logger = new TestLogger();

            ListPropertiesProvider cmdLineArgs = new ListPropertiesProvider();
            // Public args - should be written to the config file
            cmdLineArgs.AddProperty("sonar.host.url", "http://host");
            cmdLineArgs.AddProperty("public.key", "public value");

            // Sensitive values - should not be written to the config file
            cmdLineArgs.AddProperty("sonar.jdbc.username", "secret db password");
            cmdLineArgs.AddProperty("sonar.jdbc.password", "secret db password");

            ListPropertiesProvider fileSettings = new ListPropertiesProvider();
            // Public args - should be written to the config file
            fileSettings.AddProperty("file.public.key", "file public value");
            
            // Sensitive values - should not be written to the config file
            fileSettings.AddProperty("sonar.jdbc.username", "secret db password");
            fileSettings.AddProperty("sonar.jdbc.password", "secret db password");

            ProcessedArgs args = new ProcessedArgs("key", "name", "1.0", false, cmdLineArgs, fileSettings);

            IDictionary<string, string> serverProperties = new Dictionary<string, string>();

            TeamBuildSettings settings = TeamBuildSettings.CreateNonTeamBuildSettings(analysisDir);
            Directory.CreateDirectory(settings.SonarConfigDirectory); // config directory needs to exist


            // Act
            AnalysisConfig config = AnalysisConfigGenerator.GenerateFile(args, settings, serverProperties, logger);

            // Assert
            AssertConfigFileExists(config);
            logger.AssertErrorsLogged(0);
            logger.AssertWarningsLogged(0);

            // Check the config

            // "Public" arguments should be in the file
            Assert.AreEqual("key", config.SonarProjectKey, "Unexpected project key");
            Assert.AreEqual("name", config.SonarProjectName, "Unexpected project name");
            Assert.AreEqual("1.0", config.SonarProjectVersion, "Unexpected project version");

            AssertExpectedLocalSetting(SonarProperties.HostUrl, "http://host", config);
            AssertExpectedLocalSetting("file.public.key", "file public value", config);

            // Sensitive arguments should not be in the file
            AssertSettingDoesNotExist(SonarProperties.SonarUserName, config);
            AssertSettingDoesNotExist(SonarProperties.SonarPassword, config);
            AssertSettingDoesNotExist(SonarProperties.DbUserName, config);
            AssertSettingDoesNotExist(SonarProperties.DbPassword, config);
        }

        #endregion

        #region Checks

        private void AssertConfigFileExists(AnalysisConfig config)
        {
            Assert.IsNotNull(config, "Supplied config should not be null");

            Assert.IsFalse(string.IsNullOrWhiteSpace(config.FileName), "Config file name should be set");
            Assert.IsTrue(File.Exists(config.FileName), "Expecting the analysis config file to exist. Path: {0}", config.FileName);

            this.TestContext.AddResultFile(config.FileName);

        }

        private static void AssertSettingDoesNotExist(string key, AnalysisConfig actualConfig)
        {
            Property setting;
            bool found = actualConfig.GetAnalysisSettings(true).TryGetProperty(key, out setting);
            Assert.IsFalse(found, "The setting should not exist. Key: {0}", key);
        }

        private static void AssertExpectedServerSetting(string key, string expectedValue, AnalysisConfig actualConfig)
        {
            Property property;
            bool found = Property.TryGetProperty(key, actualConfig.ServerSettings, out property);

            Assert.IsTrue(found, "Expected server property was not found. Key: {0}", key);
            Assert.AreEqual(expectedValue, property.Value, "Unexpected server value. Key: {0}", key);
        }

        private static void AssertExpectedLocalSetting(string key, string expectedValue, AnalysisConfig acutalConfig)
        {
            Property property;
            bool found = Property.TryGetProperty(key, acutalConfig.LocalSettings, out property);

            Assert.IsTrue(found, "Expected local property was not found. Key: {0}", key);
            Assert.AreEqual(expectedValue, property.Value, "Unexpected local value. Key: {0}", key);
        }

        #endregion

    }
}
