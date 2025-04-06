using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    private bool initial {  get; set; }

    private ConcurrentDictionary<string, List<(int, byte[])>> _fileChunks { get; set; }
    private object _locc {  get; set; }

    private static readonly ConcurrentDictionary<string, object> _fileLocks = new();

    public ConsumerThread(ConcurrentDictionary<string, List<(int, byte[])>> sharedChunks, object _lock)
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
    }

    public void runConsumer()
    {
        while (this.isRunning)
        {
            if (this.currentFile != null)
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
                foreach (var file in _fileChunks)
                {
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
                    using (var fileStream = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 1024 * 1024))
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
                                if (_fileChunks.TryGetValue(this.currentFile, out var list))
                                {
                                    lock (list)
                                    {
                                        list.RemoveAll(c => c.Item1 == this.currentChunkIndex);
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
                                                                                                     //reset
                                            this.currentChunkIndex = 0;
                                            this.currentFile = null;
                                            this.totalChunks = 0;
                                            Console.WriteLine("Ended runConsumer writing section");
                                        }
                                    }
                                }
                            }
                            
                        }
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
    private readonly List<(int, string)> assignedFiles = new List<(int, string)>();
    private readonly object _lock = new();
    private bool initialRun = true;

    public override async Task<UploadResponse> UploadVideo(IAsyncStreamReader<VideoChunk> requestStream, 
                                                           IServerStreamWriter<UploadResponse> responseStream,
                                                           ServerCallContext context)
    {
        try
        {
            //defaults to 1
            ConsumerThread[] consumerThreads = new ConsumerThread[1];
            Thread[] threadList = new Thread[1];
            int maxBufferSize = 10;
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                if (initialRun)
                {
                    var initMsg = requestStream.Current;
                    if (initMsg.DataCase != VideoChunk.DataOneofCase.Config)
                    {
                        return new UploadResponse { CurrStatus = UploadResponse.Types.status.Error };
                    }
                    else {
                        //successful initialization
                        var numThreads = 0;
                        maxBufferSize = initMsg.Config.QueueSize;
                        if (initMsg.Config.PThreads > initMsg.Config.CThreads)
                        {
                            numThreads = initMsg.Config.CThreads;
                        } else
                        {
                            numThreads = initMsg.Config.PThreads;
                        }

                        consumerThreads = new ConsumerThread[numThreads];
                        threadList = new Thread[numThreads];

                        for (global::System.Int32 i = 0; i < numThreads; i++)
                        {
                            consumerThreads[i] = new ConsumerThread(_fileChunks, _lock);
                            threadList[i] = new Thread(consumerThreads[i].runConsumer);
                            threadList[i].Start();
                        }
                        return new UploadResponse { CurrStatus= UploadResponse.Types.status.Init };
                    }
                } else
                {
                    var chunk = requestStream.Current;
                    //stop sending
                    if (_fileChunks.Count >= maxBufferSize)
                    {
                        //tells it that the buffer is full and that the currChunk was not stored
                        //Chunk is resent as a countermeasure for dropped data
                        await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Full, CurrChunk = chunk });
                    }
                    else
                    {
                        await responseStream.WriteAsync(new UploadResponse { CurrStatus = UploadResponse.Types.status.Ok });

                        lock (_lock)
                        {
                            if (!_fileChunks.ContainsKey(chunk.VidMetadata.FileName))
                            {
                                _fileChunks[chunk.VidMetadata.FileName] = new List<(int, byte[])>();
                            }
                            _fileChunks[chunk.VidMetadata.FileName].Add((chunk.VidMetadata.ChunkIndex, chunk.VidMetadata.Data.ToByteArray()));

                            Console.WriteLine($"Received chunk {(chunk.VidMetadata.ChunkIndex) + 1}/{chunk.VidMetadata.TotalChunks} for {chunk.VidMetadata.FileName}");
                            //assignment
                            for (global::System.Int32 i = 0; i < consumerThreads.Length; i++)
                            {
                                if (consumerThreads[i].currentFile == null && !assignedFiles.Any(c => c.Item2 == chunk.VidMetadata.FileName))
                                {
                                    //assign a file
                                    var actingThread = consumerThreads[i];
                                    actingThread.currentFile = chunk.VidMetadata.FileName;
                                    actingThread.totalChunks = chunk.VidMetadata.TotalChunks;
                                    assignedFiles.Add((i, chunk.VidMetadata.FileName));
                                    Console.WriteLine($"[{i}] has been assigned the file [{actingThread.currentFile}] with a total of [{actingThread.totalChunks}]");
                                }
                            }
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