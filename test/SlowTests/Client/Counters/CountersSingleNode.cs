﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Client;
using Raven.Client.Documents.Operations.Counters;
using Raven.Client.Exceptions;
using Raven.Server.Config;
using Raven.Server.Exceptions;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace SlowTests.Client.Counters
{
    public class CountersSingleNode : RavenTestBase
    {
        [Fact]
        public void IncrementCounter()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");
                    session.SaveChanges();
                }

                store.Counters.Increment("users/1-A", "likes", 0);
                var val = store.Counters.Get("users/1-A", "likes");
                Assert.Equal(0, val);

                store.Counters.Increment("users/1-A", "likes", 10);
                val = store.Counters.Get("users/1-A", "likes");
                Assert.Equal(10, val);

                store.Counters.Increment("users/1-A", "likes", -3);
                val = store.Counters.Get("users/1-A", "likes");
                Assert.Equal(7, val);
            }
        }

        [Fact]
        public void GetCounterValue()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");
                    session.SaveChanges();
                }
                var a = store.Counters.IncrementAsync("users/1-A", "likes", 5);
                var b = store.Counters.IncrementAsync("users/1-A", "likes", 10);
                Task.WaitAll(a, b); // run them in parallel and see that they are good

                var val = store.Counters.Get("users/1-A", "likes");
                Assert.Equal(15, val);
            }
        }

        [Fact]
        public void DeleteCounter()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv1" }, "users/1-A");
                    session.Store(new User { Name = "Aviv2" }, "users/2-A");

                    session.SaveChanges();
                }

                store.Counters.Increment("users/1-A", "likes", 10);
                store.Counters.Increment("users/2-A", "likes", 20);

                store.Counters.Delete("users/1-A", "likes");
                var val = store.Counters.Get("users/1-A", "likes");
                Assert.Null(val);

                store.Counters.Delete("users/2-A", "likes");
                val = store.Counters.Get("users/2-A", "likes");
                Assert.Null(val);
            }
        }

        [Fact]
        public void MultiGet()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");
                    session.SaveChanges();
                }

                store.Counters.Increment("users/1-A", "likes", 5);
                store.Counters.Increment("users/1-A", "dislikes", 10);

                var dic = store.Counters.Get("users/1-A", new[] { "likes", "dislikes" });
                Assert.Equal(2, dic.Count);
                Assert.Equal(5, dic["likes"]);
                Assert.Equal(10, dic["dislikes"]);
            }
        }

        [Fact]
        public void MultiSetAndGetViaBatch()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");
                    session.Store(new User { Name = "Aviv2" }, "users/2-A");
                    session.SaveChanges();
                }

                var setBatch = new CounterBatch
                {
                    Documents = new List<DocumentCountersOperation>
                    {
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/1-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Increment,
                                    CounterName = "likes",
                                    Delta = 5
                                },
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Increment,
                                    CounterName = "dislikes",
                                    Delta = 10
                                }
                            }
                        },
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/2-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Increment,
                                    CounterName = "rank",
                                    Delta = 20
                                }
                            }
                        }

                    }
                };

                store.Counters.Batch(setBatch);

                var getBatch = new CounterBatch
                {
                    Documents = new List<DocumentCountersOperation>
                    {
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/1-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Get,
                                    CounterName = "likes"
                                },

                                new CounterOperation
                                {
                                    Type = CounterOperationType.Get,
                                    CounterName = "dislikes"
                                }
                            }
                        },
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/2-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Get,
                                    CounterName = "rank"
                                }
                            }
                        }
                    }
                };

                var countersDetail = store.Counters.Batch(getBatch);

                Assert.Equal(3, countersDetail.Counters.Count);

                Assert.Equal("likes", countersDetail.Counters[0].CounterName);
                Assert.Equal("users/1-A", countersDetail.Counters[0].DocumentId);
                Assert.Equal(5, countersDetail.Counters[0].TotalValue);

                Assert.Equal("dislikes", countersDetail.Counters[1].CounterName);
                Assert.Equal("users/1-A", countersDetail.Counters[1].DocumentId);
                Assert.Equal(10, countersDetail.Counters[1].TotalValue);

                Assert.Equal("rank", countersDetail.Counters[2].CounterName);
                Assert.Equal("users/2-A", countersDetail.Counters[2].DocumentId);
                Assert.Equal(20, countersDetail.Counters[2].TotalValue);

            }
        }

        [Fact]
        public void BatchWithDifferentTypesOfOperations()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv1" }, "users/1-A");
                    session.Store(new User { Name = "Aviv2" }, "users/2-A");
                    session.Store(new User { Name = "Aviv3" }, "users/3-A");

                    session.SaveChanges();
                }

                store.Counters.Increment("users/1-A", "likes", 10);
                store.Counters.Increment("users/1-A", "dislikes", 20);
                store.Counters.Increment("users/2-A", "rank", 30);
                store.Counters.Increment("users/3-A", "score", 40);

                var dic = store.Counters.Get("users/1-A", new[] {"likes", "dislikes"});
                Assert.Equal(2, dic.Count);
                Assert.Equal(10, dic["likes"]);
                Assert.Equal(20, dic["dislikes"]);

                var value = store.Counters.Get("users/2-A", "rank");
                Assert.Equal(30, value);

                value = store.Counters.Get("users/3-A", "score");
                Assert.Equal(40, value);

                var batch = new CounterBatch
                {
                    Documents = new List<DocumentCountersOperation>
                    {
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/1-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Increment,
                                    CounterName = "likes", 
                                    Delta = 100
                                },
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Delete,
                                    CounterName = "dislikes"
                                }
                            }
                        },
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/2-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Increment,
                                    CounterName = "rank",
                                    Delta = 200
                                },
                                new CounterOperation
                                {
                                    //create new counter
                                    Type = CounterOperationType.Increment,
                                    CounterName = "downloads",
                                    Delta = 300
                                }
                            }
                        },
                        new DocumentCountersOperation
                        {
                            DocumentId = "users/3-A",
                            Operations = new List<CounterOperation>
                            {
                                new CounterOperation
                                {
                                    Type = CounterOperationType.Delete,
                                    CounterName = "score"
                                }
                            }
                        }
                    }
                };

                var countersDetail = store.Counters.Batch(batch);

                Assert.Equal(3, countersDetail.Counters.Count);

                Assert.Equal("users/1-A", countersDetail.Counters[0].DocumentId);
                Assert.Equal("likes", countersDetail.Counters[0].CounterName);
                Assert.Equal(110, countersDetail.Counters[0].TotalValue);

                Assert.Equal("users/2-A", countersDetail.Counters[1].DocumentId);
                Assert.Equal("rank", countersDetail.Counters[1].CounterName);
                Assert.Equal(230, countersDetail.Counters[1].TotalValue);

                Assert.Equal("users/2-A", countersDetail.Counters[2].DocumentId);
                Assert.Equal("downloads", countersDetail.Counters[2].CounterName);
                Assert.Equal(300, countersDetail.Counters[2].TotalValue);

                Assert.Null(store.Counters.Get("users/1-A", "dislikes"));
                Assert.Null(store.Counters.Get("users/3-A", "score"));

            }
        }

        [Fact]
        public void DeleteCreateWithSameNameDeleteAgain()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");

                    session.SaveChanges();
                }

                store.Counters.Increment("users/1-A", "likes", 10);
                var val = store.Counters.Get("users/1-A", "likes");
                Assert.Equal(10, val);

                store.Counters.Delete("users/1-A", "likes");
                val = store.Counters.Get("users/1-A", "likes");
                Assert.Null(val);

                store.Counters.Increment("users/1-A", "likes", 20);
                val = store.Counters.Get("users/1-A", "likes");
                Assert.Equal(20, val);

                store.Counters.Delete("users/1-A", "likes"); 
                val = store.Counters.Get("users/1-A", "likes");
                Assert.Null(val);
            }
        }

        [Fact]
        public void IncrementAndDeleteShouldChangeDocumentMetadata()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");
                    session.SaveChanges();
                }

                store.Counters.Increment("users/1-A", "likes", 10);

                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");
                    var metadatad = session.Advanced.GetMetadataFor(user);

                    Assert.True(metadatad.TryGetValue(Constants.Documents.Metadata.Counters, out object counters));
                    Assert.Equal(1, ((object[])counters).Length);
                    Assert.True(((object[])counters).Contains("likes"));
                }

                store.Counters.Increment("users/1-A", "votes", 50);
                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");
                    var metadatad = session.Advanced.GetMetadataFor(user);

                    Assert.True(metadatad.TryGetValue(Constants.Documents.Metadata.Counters, out object counters));
                    Assert.Equal(2, ((object[])counters).Length);
                    Assert.True(((object[])counters).Contains("likes"));
                    Assert.True(((object[])counters).Contains("votes"));
                }

                store.Counters.Delete("users/1-A", "likes");

                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");
                    var metadatad = session.Advanced.GetMetadataFor(user);

                    Assert.True(metadatad.TryGetValue(Constants.Documents.Metadata.Counters, out object counters));
                    Assert.Equal(1, ((object[])counters).Length);
                    Assert.True(((object[])counters).Contains("votes"));
                }

                store.Counters.Delete("users/1-A", "votes");
                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");
                    var metadatad = session.Advanced.GetMetadataFor(user);
                    Assert.False(metadatad.TryGetValue(Constants.Documents.Metadata.Counters, out _));
                }

            }
        }

        [Fact]
        public void CounterNameShouldPreserveCase()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Aviv" }, "users/1-A");
                    session.Advanced.Counters.Increment("users/1-A", "Likes", 10);
                    session.SaveChanges();
                }


                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");
                    var val = session.Advanced.Counters.Get(user, "Likes");
                    Assert.Equal(10, val);

                    var counters = session.Advanced.GetCountersFor(user);
                    Assert.Equal("Likes", counters[0]);

                }

            }
        }

        [Fact]
        public void CreatingCounterWithFeaturesAvailabilitySetToStableWillThrow()
        {
            using (var store = GetDocumentStore(new Options
            {
                ModifyDatabaseRecord = record =>
                {
                    record.Settings[RavenConfiguration.GetKey(x => x.Core.FeaturesAvailability)] = null; // by default we should have Stable features
                }
            }))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User(), "users/1-A");
                    session.SaveChanges();
                }
                var ex = Assert.Throws<RavenException>(() => store.Counters.Increment("users/1-A", "Likes"));
                Assert.StartsWith("Raven.Server.Exceptions.FeaturesAvailabilityException", ex.Message);
            }
        }
    }
}