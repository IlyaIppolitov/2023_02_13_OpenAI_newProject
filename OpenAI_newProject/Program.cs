using Newtonsoft.Json.Linq;
using OpenAI_API;
using OpenAI_API.Completions;
using OpenAI_API.Models;
using System;
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static System.Net.Mime.MediaTypeNames;


// инициализация ключей
string? keyOpenAI = Environment.GetEnvironmentVariable("openai_Rodion");
//string? keyOpenAI = Environment.GetEnvironmentVariable("openai_api_key");
if (keyOpenAI == null)
    throw new InvalidOperationException($"KeyOpenAI is not supported.");

string? keyTlgrm = Environment.GetEnvironmentVariable("telegram_first");
if (keyTlgrm == null)
    throw new InvalidOperationException($"keyTlgrm is not supported.");


var api = new OpenAIAPI(new APIAuthentication(keyOpenAI));

// Инициализация подключения к боту по ключу
var botClient = new TelegramBotClient(keyTlgrm);

// Токен отмены
using CancellationTokenSource cts = new();


// Инициализация словаря с текущим статусом каждого чата
ConcurrentDictionary<long, UserState> _clientStates = new ConcurrentDictionary<long, UserState>();

// Определение временного интервала проверки в секундах
const int timeLimit = 10;
// Определение количетсва допустимых сообщений за временной интервал
const int maxQuantity = 10;

// StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
ReceiverOptions receiverOptions = new()
{
    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
};

botClient.StartReceiving(
    updateHandler: HandleUpdateAsync,
    pollingErrorHandler: HandlePollingErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

// Подключение к боту
var me = await botClient.GetMeAsync();

Console.WriteLine($"Start listening for @{me.Username}");
Console.ReadLine();

// Send cancellation request to stop bot
cts.Cancel();


async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    if (update.Message is not { } message)
        return;

    if (message.Text is not { } text)
        return;

    if (text.ToLower() == "/start")
    {
        await SendAnswerToStartMsg(message, botClient, cancellationToken);
        return;
    }

    // Предварительная обработка запроса 

    // проверка существования ключа с указанным в библиотеке ключей
    if (_clientStates.ContainsKey(message.Chat.Id))
    {
        // проверка количества сообщений в этом чате
        if (_clientStates[message.Chat.Id].dateTimes.Count == maxQuantity)
        {
            // проверка прошедшего времени от первого сообщения в очереди
            int timeLeft = timeLimit - (message.Date - _clientStates[message.Chat.Id].dateTimes.First()).Seconds;
            if (timeLeft > 0)
            {
                await SendTextMsg(message, $"Отдохни сам и дай отдохнуть нам ещё {timeLeft} секунд!", botClient, cancellationToken);
                return;
            }
            // удаление самого раннего времени из очереди
            _clientStates[message.Chat.Id].dateTimes.TryDequeue(out _);
        }
    }
    else
    {
        _clientStates[message.Chat.Id] = new UserState();
    };
    // добавление в очередь время текущего запроса
    _clientStates[message.Chat.Id].dateTimes.Enqueue(message.Date);

    // Простой запрос
    //var result = await api.Completions.CreateCompletionAsync(new CompletionRequest(text, model: Model.DavinciText, max_tokens: 100, temperature: 0.2));
    //await SendTextMsg(message, result.ToString(), botClient, cancellationToken);
    //return;

    string answerText = "Here is your answer:\n";
    string tempText = string.Empty;

    // Отправка вступительного сообшения для получения Id сообщения
    var firstMessage = await botClient.SendTextMessageAsync(
    chatId: message.Chat.Id,
    text: answerText,
    cancellationToken: cancellationToken);

    // Получение ответа от OpenAI по частям
    await api.Completions.StreamCompletionAsync(
    new CompletionRequest(message.Text, Model.DavinciText, max_tokens: 20, temperature: 0.3, presencePenalty: 0.5, frequencyPenalty: 0.1),
    async res =>
    {
        answerText += res.ToString();
        if (tempText != answerText)
        {
            tempText = answerText;
            try
            {
                await botClient.EditMessageTextAsync(
                    chatId: message.Chat.Id,
                    messageId: firstMessage.MessageId,
                    text: tempText,
                    cancellationToken: cancellationToken);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {
                if (ex.Message != "Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message")
                //if (ex.ErrorCode != 400)
                {
                    throw new Telegram.Bot.Exceptions.ApiRequestException(ex.Message, ex);
                }
            }
        }
    });
    return;
}


Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    var ErrorMessage = exception switch
    {
        ApiRequestException apiRequestException
            => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
        _ => exception.ToString()
    };

    Console.WriteLine(ErrorMessage);
    return Task.CompletedTask;
}

// Отправка приветствия в ответ на сообщение начальное сообщение /start
async Task SendAnswerToStartMsg(Message message, ITelegramBotClient botClient, CancellationToken cancellationToken)
{
    if (message.From is not { } sender)
        return;

    string textAnswer;

    if (sender.IsBot)
    {
        textAnswer = "Никаких имён для ботов!";
    }
    else
    {
        textAnswer = $"Привет, рад тебя видеть {message.From.FirstName}!\nВведите ваш запрос!";
    }

    await botClient.SendTextMessageAsync(
        chatId: message.Chat.Id,
        text: textAnswer,
        cancellationToken: cancellationToken);

    Console.WriteLine($"Received a /start message in chat {message.Chat.Id}.");
    return;
}

// Отправка сообщения по умолчанию
async Task SendTextMsg(Message message, string text, ITelegramBotClient botClient, CancellationToken cancellationToken)
{

    await botClient.SendTextMessageAsync(
        chatId: message.Chat.Id,
        text: text,
        cancellationToken: cancellationToken);
}