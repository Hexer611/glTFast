// SPDX-FileCopyrightText: 2023 Unity Technologies and the glTFast authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GLTFast.Logging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GLTFast.Tests.Import
{
    [TestFixture]
    class LoadTests
    {
        [GltfTestCase("glTF-test-models", 22)]
        public IEnumerator GltfTestModels(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            Debug.Log($"Loading {testCase}");
            var go = new GameObject();
            var deferAgent = new UninterruptedDeferAgent();
            var loadLogger = new CollectingLogger();
            var rootDir = testCaseSet.GetSourcePath();
            var path = Path.Combine(rootDir, testCase.relativeUri);

            using (var gltf = new GltfImport(deferAgent: deferAgent, logger: loadLogger))
            {
                var task = gltf.Load(path);
                yield return WaitForTask(task);
                var success = task.Result;
                if (success ^ !testCase.expectLoadFail)
                {
                    AssertLoggers(new[] { loadLogger }, testCase);
                    if (success)
                    {
                        throw new AssertionException("glTF import unexpectedly succeeded!");
                    }

                    throw new AssertionException("glTF import failed!");
                }

                if (!success)
                {
                    AssertLoggers(new[] { loadLogger }, testCase);
                    yield break;
                }
                var instantiateLogger = new CollectingLogger();
                var instantiator = new GameObjectInstantiator(gltf, go.transform, instantiateLogger);
                task = gltf.InstantiateMainSceneAsync(instantiator);
                yield return WaitForTask(task);
                success = task.Result;
                if (!success)
                {
                    instantiateLogger.LogAll();
                    throw new AssertionException("glTF instantiation failed");
                }
                Object.Destroy(go);
                AssertLoggers(new[] { loadLogger, instantiateLogger }, testCase);
            }
        }

        static void AssertLoggers(IEnumerable<CollectingLogger> loggers, GltfTestCase testCase)
        {
            var expectedLogCodeFound = new Dictionary<LogCode, bool>();
            foreach (var logCode in testCase.expectedLogCodes)
            {
                expectedLogCodeFound[logCode] = false;
            }

            foreach (var logger in loggers)
            {
                if (logger.Count < 1) continue;
                foreach (var item in logger.Items)
                {
                    switch (item.Type)
                    {
                        case LogType.Assert:
                        case LogType.Error:
                        case LogType.Exception:
                            if (expectedLogCodeFound.Keys.Contains(item.Code))
                            {
                                expectedLogCodeFound[item.Code] = true;
                                // Informal log
                                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, item.ToString());
                            }
                            else
                            {
                                item.Log();
                                throw new AssertionException($"Unhandled {item.Type} message {item} ({item.Code}).");
                            }
                            break;
                        case LogType.Warning:
                        case LogType.Log:
                        default:
                            item.Log();
                            break;
                    }
                }
            }

            foreach (var b in expectedLogCodeFound.Where(b => !b.Value))
            {
                throw new AssertionException($"Missing expected log message {b.Key}.");
            }
        }

        /// <summary>
        /// Wraps a <see cref="Task"/> in an <see cref="IEnumerator"/>.
        /// </summary>
        /// <param name="task">The async Task to wait form</param>
        /// <param name="timeout">Optional timeout in seconds</param>
        /// <returns>IEnumerator</returns>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="TimeoutException">Thrown when a timout was set and the task took too long</exception>
        static IEnumerator WaitForTask(Task task, float timeout = -1)
        {
            var startTime = Time.realtimeSinceStartup;

            while (!task.IsCompleted)
            {
                CheckExceptionAndTimeout();
                yield return null;
            }

            CheckExceptionAndTimeout();
            yield break;

            void CheckExceptionAndTimeout()
            {
                if (task.Exception != null)
                    throw task.Exception;
                if (timeout > 0 && Time.realtimeSinceStartup - startTime > timeout)
                {
                    throw new TimeoutException();
                }
            }
        }
    }
}