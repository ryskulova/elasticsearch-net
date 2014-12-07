﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Nest.Tests.MockData.Domain;

namespace Nest.Tests.Integration.Core.Repository
{
	[TestFixture]
	public class RestoreTests : IntegrationTests
	{
	    private string _indexName;
	    private string _repositoryName;
	    private string _backupName;
	    //private ElasticsearchProject _elasticsearchProject;
	    private List<ElasticsearchProject> _indexedElements = new List<ElasticsearchProject>();
	    private string _restoredIndexName;

	    [SetUp]
	    public void Setup()
	    {
	        _indexName = ElasticsearchConfiguration.NewUniqueIndexName();
	        _repositoryName = ElasticsearchConfiguration.NewUniqueIndexName();
	        _backupName = ElasticsearchConfiguration.NewUniqueIndexName();

            for (int i = 0; i < 100; i++)
            {
                var elementToIndex = new ElasticsearchProject()
                {
                    Id = i,
                    Name = "Coboles",
                    Content = "COBOL elasticsearch client"
                };
                var indexResponse = Client.Index(elementToIndex, d => d.Index(_indexName).Refresh(true));
                _indexedElements.Add(elementToIndex);
            }

	        this.Client.CreateRepository(_repositoryName, r => r
	            .FileSystem(@"local\\path", o => o
	                .Compress()
	                .ConcurrentStreams(10)));
	    }

	    [TearDown]
	    public void TearDown()
	    {
            var deleteReposResult = this.Client.DeleteRepository(_repositoryName);
	        this.Client.DeleteIndex(_indexName);
	        this.Client.DeleteIndex(_restoredIndexName);
	    }

	    [Test]
		public void SnapshotRestore()
	    {
			var snapshotResponse = this.Client.Snapshot(_repositoryName, _backupName, selector: f => f
				.Index(_indexName)
				.WaitForCompletion(true)
				.IgnoreUnavailable()
				.Partial());
			snapshotResponse.IsValid.Should().BeTrue();
			snapshotResponse.Accepted.Should().BeTrue();
			snapshotResponse.Snapshot.Should().NotBeNull();
			snapshotResponse.Snapshot.EndTimeInMilliseconds.Should().BeGreaterThan(0);
			snapshotResponse.Snapshot.StartTime.Should().BeAfter(DateTime.UtcNow.AddDays(-1));

			var d = ElasticsearchConfiguration.DefaultIndex;
			var restoreResponse = this.Client.Restore(_repositoryName, _backupName, r => r
				.WaitForCompletion(true)
				.RenamePattern(d + "_(.+)")
				.RenameReplacement(d + "_restored_$1")
				.Index(_indexName)
				.IgnoreUnavailable(true));

			_restoredIndexName = _indexName.Replace(d +  "_", d + "_restored_");
			restoreResponse.IsValid.Should().BeTrue();
			restoreResponse.Snapshot.Should().NotBeNull();
			restoreResponse.Snapshot.Name.Should().Be(_backupName);
			restoreResponse.Snapshot.Indices.Should().Equal(new string[] { _restoredIndexName });

			var indexExistsResponse = this.Client.IndexExists(f => f.Index(_restoredIndexName));
			indexExistsResponse.Exists.Should().BeTrue();

            var count = this.Client.Count<ElasticsearchProject>(descriptor => descriptor.Index(_restoredIndexName)).Count;

	        var indexContent = this.Client.SourceMany<ElasticsearchProject>(_indexedElements.Select(x => (long)x.Id), _restoredIndexName);

	        count.Should().Be(_indexedElements.Count);
            indexContent.ShouldBeEquivalentTo(_indexedElements);
		}

	    [Test]
	    public void SnapshotRestoreObservable()
	    {
	        var snapshotObservable = this.Client.SnapshotObservable(TimeSpan.FromMilliseconds(100), descriptor => descriptor
                .Repository(_repositoryName)
                .Snapshot(_backupName)
	            .Index(_indexName));
            
	        bool snapshotCompleted = false;

            var snapshotObserver = new Observer<ISnapshotStatusResponse>(
	                onNext: r =>
	                {
	                    var snapshotsCount = r.Snapshots.Count();
                        Assert.IsTrue(r.IsValid);
                        Assert.AreEqual(1, snapshotsCount);
                        CollectionAssert.Contains(r.Snapshots.ElementAt(0).Indices.Keys, _indexName);
	                },
	                onError: e =>
	                {
                        Assert.Fail(e.Message);
                        snapshotCompleted = true;
	                },
	                completed: () =>
                    {
                        snapshotCompleted = true;
	                }
                );

	        using (var observable = snapshotObservable.Subscribe(snapshotObserver))
            {
                while (!snapshotCompleted)
                {
                    Thread.Sleep(100);
                }
	        }

            var getSnapshotResponse = this.Client.GetSnapshot(_repositoryName, _backupName, descriptor => descriptor);
            var snapshot = getSnapshotResponse.Snapshots.ElementAt(0);
            Assert.IsTrue(getSnapshotResponse.IsValid);
            Assert.AreEqual(1, getSnapshotResponse.Snapshots.Count());
            Assert.AreEqual("SUCCESS", snapshot.State);
            CollectionAssert.Contains(snapshot.Indices, _indexName);

            var d = ElasticsearchConfiguration.DefaultIndex;
            var restoreObservable = this.Client.RestoreObservable(TimeSpan.FromMilliseconds(1), r => r
                .Repository(_repositoryName)
                .Snapshot(_backupName)
                .RenamePattern(d + "_(.+)")
                .RenameReplacement(d + "_restored_$1")
                .Index(_indexName)
                .IgnoreUnavailable(true));

	        bool restoreCompleted = false;
	        var restoreObserver = new Observer<IRecoveryStatusResponse>(
	            onNext: r =>
	            {
	                var index = r.Indices.FirstOrDefault();
                    Assert.AreEqual(1, r.Indices.Count);
	            },
	            onError: e =>
                {
                    Assert.Fail(e.Message);
                    restoreCompleted = true;
	            },
	            completed: () =>
                {
                    restoreCompleted = true;
	            }
	            );

	        using (var observable = restoreObservable.Subscribe(restoreObserver))
	        {
	            while (!restoreCompleted) Thread.Sleep(100);
	        }

            _restoredIndexName = _indexName.Replace(d + "_", d + "_restored_");
            var restoredIndexExistsResponse = this.Client.IndexExists(f => f.Index(_restoredIndexName));
            restoredIndexExistsResponse.Exists.Should().BeTrue();

	        var count = this.Client.Count<ElasticsearchProject>(descriptor => descriptor.Index(_restoredIndexName)).Count;
            var indexContent = this.Client.SourceMany<ElasticsearchProject>(_indexedElements.Select(x => (long)x.Id), _restoredIndexName);

            count.Should().Be(_indexedElements.Count);
            indexContent.ShouldBeEquivalentTo(_indexedElements);
	    }
	}
}
