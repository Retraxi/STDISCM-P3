using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.CookiePolicy;
using VideoProto; // Import generated gRPC classes


//Consumer thread class
public class ConsumerThread
{
    //self-buffer
    private List<(int, byte[])> fileChunks;
    public string? currentFile { get; set; }
    public int currentChunkIndex { get; set; }
    public int totalChunks { get; set; }
    private bool isRunning { get; set; }
    public bool fileCompleted { get; set; }
    private bool initial { get; set; }

    private ConcurrentDictionary<string, List<(int, byte[])>> _fileChunks { get; set; }
    private ConcurrentDictionary<string, int> _fileSizes { get; set; }
    private object _locc { get; set; }

    private static readonly ConcurrentDictionary<string, object> _fileLocks = new();

    private ConcurrentQueue<string> fileAssignmentQueue { get; set; }

    private HashSet<string> completedFiles { get; set; }

    public ConsumerThread(ConcurrentDictionary<string, List<(int, byte[])>> sharedChunks,
                          object _lock, HashSet<string> sharedCompletedFiles,
                          ConcurrentDictionary<string, int> sharedSizes,
                          ConcurrentQueue<string> fAQueue)
    {
        fileChunks = new List<(int, byte[])>();
        currentFile = null;
        currentChunkIndex = 0;
        totalChunks = 0;
        isRunning = true;
        fileCompleted = false;
        _fileChunks = sharedChunks;
        _locc = _lock;
        initial = true;
        completedFiles = sharedCompletedFiles;
        _fileSizes = sharedSizes;
        fileAssignmentQueue = fAQueue;
    }

    public void runConsumer()
    {
        while (this.isRunning)
        {
            if (this.currentFile == null)
            {
                if (fileAssignmentQueue.TryDequeue(out var file))
                {
                    Console.WriteLine();
                    if (_fileChunks.TryGetValue(file, out var chunks) && _fileSizes.TryGetValue(file, out var tChunks))
                    {
                        this.currentFile = file;
                        this.totalChunks = tChunks; // You might also pass TotalChunks in metadata
                        this.currentChunkIndex = 0;
                        this.initial = true;
                    }
                }
                else
                {
                    Thread.Sleep(20);
                    continue;
                }
            }
            else
            {
                string outputPath = Path.Combine("UploadedVideos", this.currentFile);
                if (initial)
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    initial = false;
                }
                //Console.WriteLine($"Iterating through filechunks: {file.Key}");
                List<(int, byte[])> snapshot;
                if (_fileChunks.TryGetValue(this.currentFile, out var list))
                {
                    lock (list)
                    {
                        snapshot = list.OrderBy(c => c.Item1).ToList();
                    }
                    fileChunks = snapshot;
                }

                //Console.WriteLine($"Current Chunks: [{file.Value.Count}]");
                //var fileName = file.Key
                //Console.WriteLine("Entered runConsumer writing section");

                //Console.WriteLine($"Total number of chunk in sortedChunks: {sortedChunks.Count}");

                bool chunkWritten = false;

                Directory.CreateDirectory("UploadedVideos");

                var fileLock = _fileLocks.GetOrAdd(outputPath, _ => new object());


                lock (fileLock)
                {
                    try
                    {
                        using (var fileStream = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024))
                        {
                            foreach (var chunk in fileChunks)
                            {
                                if (this.currentChunkIndex == chunk.Item1)
                                {
                                    //Console.WriteLine("Chunk being written
                                    fileStream.Write(chunk.Item2, 0, chunk.Item2.Length);
                                    fileStream.Flush();
                                    Console.WriteLine($"[{this.currentChunkIndex} has been written]");

                                    //clear of processed chunks
                                    if (_fileChunks.TryGetValue(this.currentFile, out var chunkBytes))
                                    {
                                        lock (chunkBytes)
                                        {
                                            chunkBytes.RemoveAll(c => c.Item1 == this.currentChunkIndex);
                                        }
                                    }
                                    this.currentChunkIndex++;
                                    chunkWritten = true;
                                    break;
                                    //Console.WriteLine("Chunk completely written.");
                                }
                            }
                            if (!chunkWritten)
                            {
                                Thread.Sleep(50);
                            }
                            if (this.currentChunkIndex == this.totalChunks)
                            {
                                if (_fileChunks.TryGetValue(this.currentFile, out var remaining))
                                {
                                    if (remaining != null)
                                    { //not null
                                        lock (remaining)
                                        {
                                            if (remaining.Count == 0)
                                            {
                                                Console.WriteLine($"currentChunk [{this.currentChunkIndex}] | totalChunks [{this.totalChunks}]");
                                                Console.WriteLine($"File {this.currentFile} assembled successfully.");
                                                _fileChunks.TryRemove(this.currentFile, out var removedList); //remove the file and all of its chunks
                                                completedFiles.Add(this.currentFile);
                                                this.currentChunkIndex = 0;
                                                this.currentFile = null;
                                                this.totalChunks = 0;
                                                this.initial = true;

                                                Console.WriteLine("Ended runConsumer writing section");
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.StackTrace);
                    }
                }
            }
        }
    }
}

public class VideoConsumer : VideoService.VideoServiceBase
{
    //Shared Buffer
    private readonly ConcurrentDictionary<string, List<(int, byte[])>> _fileChunks = new();
    private readonly ConcurrentDictionary<string, int> _fileSizes = new();
    //private readonly List<(int, string)> assignedFiles = new List<(int, string)>();
    private readonly object _lock = new();
    private readonly HashSet<string> completedFiles = new(); // Track when consumer finishes a file
    private readonly ConcurrentQueue<string> fileAssignmentQueue = new();
    private readonly HashSet<string> enqueuedFiles = new(); // Prevents duplicate enqueue


    public override async Task<UploadResponse> UploadVideo(IAsyncStreamReader<VideoChunk> requestStream,
                                                           IServerStreamWriter<UploadResponse> responseStream,
                                                           ServerCallContext context)
    {
        bool initialRun = true;
        int totalExpected = 0;
        try
        {
            //defaults to 1
            ConsumerThread[] consumerThreads = new ConsumerThread[1];
            Thread[] threadList = new Thread[1];
            int maxBufferSize = 10;
            while (await requestStream.MoveNext(context.CancellationToken) || !(completedFiles.Count == totalExpected))
            {
                if (initialRun)
                {
                    var initMsg = requestStream.Current;
                    if (initMsg.DataCase != VideoChunk.DataOneofCase.Config)
                    {
                        //Console.WriteLine("Lily died");
                        Console.WriteLine(initialRun);
                        await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Error });
                    }
                    else
                    {
                        //successful initialization
                        var numThreads = 0;
                        maxBufferSize = initMsg.Config.QueueSize;
                        if (initMsg.Config.PThreads > initMsg.Config.CThreads)
                        {
                            numThreads = initMsg.Config.CThreads;
                        }
                        else
                        {
                            numThreads = initMsg.Config.PThreads;
                        }

                        consumerThreads = new ConsumerThread[numThreads];
                        threadList = new Thread[numThreads];

                        for (global::System.Int32 i = 0; i < numThreads; i++)
                        {
                            consumerThreads[i] = new ConsumerThread(_fileChunks, _lock, completedFiles, _fileSizes, fileAssignmentQueue);
                            threadList[i] = new Thread(consumerThreads[i].runConsumer);
                            threadList[i].Start();
                        }
                        totalExpected = initMsg.Config.TotalCount;

                        lock (_lock)
                        {
                            initialRun = false;
                        }
                        //Console.WriteLine($"LILY: {initialRun}");
                        await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Init });
                    }
                }
                else
                {
                    //Console.WriteLine("LILY LILY");
                    var chunk = requestStream.Current;
                    int totalByteArrays = (_fileChunks.Values.Sum(list => list.Count)) / threadList.Length;
                    //stop sending
                    if (totalByteArrays >= maxBufferSize)
                    {
                        //tells it that the buffer is full and that the currChunk was not stored
                        //Chunk is resent as a countermeasure for dropped data
                        await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Full, CurrChunk = chunk });
                    }
                    else
                    {
                        if (chunk == null)
                        {
                            await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Error});
                        }
                        else
                        {
                            lock (_lock)
                            {
                                if (!_fileChunks.ContainsKey(chunk.VidMetadata.FileName))
                                {
                                    _fileChunks[chunk.VidMetadata.FileName] = new List<(int, byte[])>();
                                    if (!enqueuedFiles.Contains(chunk.VidMetadata.FileName))
                                    {
                                        fileAssignmentQueue.Enqueue(chunk.VidMetadata.FileName);
                                        enqueuedFiles.Add(chunk.VidMetadata.FileName);
                                        Console.WriteLine($"Enqueued file {chunk.VidMetadata.FileName} for assignment.");
                                    }
                                }
                                _fileChunks[chunk.VidMetadata.FileName].Add((chunk.VidMetadata.ChunkIndex, chunk.VidMetadata.Data.ToByteArray()));
                                _fileSizes[chunk.VidMetadata.FileName] = chunk.VidMetadata.TotalChunks;
                                Console.WriteLine($"Received chunk {(chunk.VidMetadata.ChunkIndex) + 1}/{chunk.VidMetadata.TotalChunks} for {chunk.VidMetadata.FileName}");
                                //assignment
                                //for (global::System.Int32 i = 0; i < consumerThreads.Length; i++)
                                //{
                                //    if (consumerThreads[i].currentFile == null && !assignedFiles.Any(c => c.Item2 == chunk.VidMetadata.FileName))
                                //    {
                                //        //assign a file
                                //        var actingThread = consumerThreads[i];
                                //        actingThread.currentFile = chunk.VidMetadata.FileName;
                                //        actingThread.totalChunks = chunk.VidMetadata.TotalChunks;
                                //        assignedFiles.Add((i, chunk.VidMetadata.FileName));
                                //        Console.WriteLine($"[{i}] has been assigned the file [{actingThread.currentFile}] with a total of [{actingThread.totalChunks}]");
                                //    }
                                //}
                            }
                            await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Ok });
                        }
                    }
                }
            }
            return new UploadResponse { CurrStatus = UploadResponse.Types.status.Complete };
        }
        catch (Exception ex)
        {
            return new UploadResponse { CurrStatus = UploadResponse.Types.status.Wait };
        }
    }

}