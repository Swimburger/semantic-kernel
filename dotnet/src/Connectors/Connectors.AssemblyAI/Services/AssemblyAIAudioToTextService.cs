﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssemblyAI;
using AssemblyAI.Transcripts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.AudioToText;

namespace Microsoft.SemanticKernel.Connectors.AssemblyAI;

/// <summary>
/// AssemblyAI speech-to-text service.
/// </summary>
public sealed class AssemblyAIAudioToTextService : IAudioToTextService
{
    private readonly AssemblyAIClient _client;

    /// <summary>
    /// Attributes is not used by AssemblyAIAudioToTextService.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    /// <summary>
    /// Creates an instance of the <see cref="AssemblyAIAudioToTextService"/> with an AssemblyAI API key.
    /// </summary>
    /// <param name="apiKey">AssemblyAI API key</param>
    /// <param name="endpoint">Optional endpoint uri including the port where AssemblyAI server is hosted</param>
    /// <param name="httpClient">Optional HTTP client to be used for communication with the AssemblyAI API.</param>
    /// <param name="loggerFactory">Optional logger factory to be used for logging.</param>
    public AssemblyAIAudioToTextService(
        string apiKey,
        Uri? endpoint = null,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null
    )
    {
        Verify.NotNullOrWhiteSpace(apiKey);
        this._client = AssemblyAIClientFactory.Create(apiKey, endpoint, httpClient, loggerFactory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TextContent>> GetTextContentsAsync(
        AudioContent content,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default
    )
    {
        Verify.NotNull(content);

        if (executionSettings?.ExtensionData is not null && executionSettings.ExtensionData.Count > 0)
        {
            throw new ArgumentException("ExtensionData is not supported by AssemblyAI, use AssemblyAIAudioToTextExecutionSettings.TranscriptParams.", nameof(executionSettings));
        }

        string uploadUrl;
        if (content.Data is { IsEmpty: false })
        {
            var response = await this._client.Files.UploadAsync(
                content.Data.Value,
                null,
                cancellationToken
            ).ConfigureAwait(false);
            uploadUrl = response.UploadUrl;
        }
        else if (content.Uri is not null)
        {
            // to prevent unintentional file uploads by injection attack
            if (content.Uri.IsFile)
            {
                throw new ArgumentException("File URI is not supported.");
            }

            uploadUrl = content.Uri.ToString();
        }
        else
        {
            throw new ArgumentException("AudioContent doesn't have any content.", nameof(content));
        }

        TimeSpan? pollingInterval = null;
        TimeSpan? pollingTimeout = null;
        TranscriptOptionalParams? transcriptParams = null;
        if (executionSettings is AssemblyAIAudioToTextExecutionSettings aaiExecSettings)
        {
            pollingInterval = aaiExecSettings.PollingInterval;
            pollingTimeout = aaiExecSettings.PollingTimeout;
            transcriptParams = aaiExecSettings.TranscriptParams;
        }

        Transcript transcript = await this._client.Transcripts.SubmitAsync(
                new Uri(uploadUrl),
                transcriptParams ?? new TranscriptOptionalParams(),
                null,
                cancellationToken
            )
            .ConfigureAwait(false);

        transcript = await this._client.Transcripts.WaitUntilReady(
            transcript.Id,
            pollingInterval: pollingInterval,
            pollingTimeout: pollingTimeout,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        transcript.EnsureStatusCompleted();

        return
        [
            new TextContent(
                text: transcript.Text,
                modelId: null,
                innerContent: transcript,
                encoding: Encoding.UTF8,
                metadata: null
            )
        ];
    }
}
