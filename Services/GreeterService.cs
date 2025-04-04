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
    public int? currentChunkIndex { get; set; }
    public int? totalChunks { get; set; }
    private bool isRunning { get; set; }
    public bool fileCompleted { get; set; }

    public ConcurrentDictionary<string, List<(int, byte[])>> _fileChunks { get; set; }

    public ConsumerThread(ConcurrentDictionary<string, List<(int, byte[])>> sharedChunks)
    {
        fileChunks = new List<(int, byte[])>();
        currentFile = null;
        currentChunkIndex = 0;
        totalChunks = 0;
        isRunning = true;
        fileCompleted = false;
        _fileChunks = sharedChunks;
    }

    public void runConsumer()
    {
        while (this.isRunning)
        {
            if (this.currentFile != null)
            {
                foreach (var file in _fileChunks)
                {
                    Console.WriteLine($"Iterating through filechunks: {file.Key}");
                    if(this.currentFile == file.Key)
                    {
                        //var fileName = file.Key
                        Console.WriteLine("Entered runConsumer writing section");
                        var sortedChunks = file.Value.OrderBy(c => c.Item1).Select(c => c.Item2).ToList();
                        string outputPath = Path.Combine("UploadedVideos", this.currentFile);

                        Directory.CreateDirectory("UploadedVideos");

                        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                        foreach (var chunk in sortedChunks)
                        {
                            fileStream.Write(chunk, 0, chunk.Length);
                            this.currentChunkIndex++;
                        }

                        if (this.currentChunkIndex == this.totalChunks)
                        {
                            Console.WriteLine($"File {this.currentFile} assembled successfully.");
                            _fileChunks.TryRemove(currentFile, out var removedList); //remove the file and all of its chunks
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

public class VideoConsumer : VideoService.VideoServiceBase
{
    //Shared Buffer
    private readonly ConcurrentDictionary<string, List<(int, byte[])>> _fileChunks = new();
    private readonly List<(int, string)> assignedFiles = new List<(int, string)>();
    private readonly object _lock = new();
    private bool initialRun = false;

    public override async Task<UploadResponse> UploadVideo(IAsyncStreamReader<VideoChunk> requestStream, 
                                                           IServerStreamWriter<UploadResponse> responseStream,
                                                           ServerCallContext context)
    {
        try
        {
            ConsumerThread[] consumerThreads = new ConsumerThread[1];
            Thread[] threadList = new Thread[1];
            int maxBufferSize = 100;
            consumerThreads[0] = new ConsumerThread(_fileChunks);
            threadList[0] = new Thread(consumerThreads[0].runConsumer);
            threadList[0].Start();
            if (initialRun) { 
                //initialization
                await requestStream.MoveNext(context.CancellationToken);

                var chunk = requestStream.Current;

                ////readFromFile
                //if(chunk.Config != null)
                //{
                //    //check if pThreads are higher than cThreads
                //    if (chunk.Config.PThreads > )
                //    {
                        
                //    }
                //}

            }
            while (await requestStream.MoveNext(context.CancellationToken))
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
                        {//assignedFiles.Any(tuple => tuple.Item2 != chunk.VidMetadata.FileName) && 
                            if (consumerThreads[i].currentFile == null)
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
            return new UploadResponse { CurrStatus = UploadResponse.Types.status.Complete };
        }
        catch (Exception ex)
        {
            return new UploadResponse { CurrStatus = UploadResponse.Types.status.Wait };
        }
    }

}