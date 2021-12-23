using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AIMLTGBot
{
    enum ChatMode
    {
        CHATTING,
        RECOGNIZING,
        CHOOSING
    };
    public class TelegramService : IDisposable
    {
        private readonly TelegramBotClient client;
        private readonly AIMLService aimlService;
        private readonly NeuralNetworkService networkService;
        private readonly CLIPSService clipsService;
        private string lastRecognizedLetter = "none";

        Dictionary<long,ChatMode> dialogMode;
        // CancellationToken - инструмент для отмены задач, запущенных в отдельном потоке
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        public string Username { get; }

        public TelegramService(string token, AIMLService aimlService)
        {
            this.aimlService = aimlService;
            networkService = new NeuralNetworkService();
            clipsService = new CLIPSService();
            client = new TelegramBotClient(token);
            dialogMode = new Dictionary<long, ChatMode>();
            
            client.StartReceiving(HandleUpdateMessageAsync, HandleErrorAsync, new ReceiverOptions
            {   // Подписываемся только на сообщения
                AllowedUpdates = new[] { UpdateType.Message }
            },
            cancellationToken: cts.Token);
            // Пробуем получить логин бота - тестируем соединение и токен
            Username = client.GetMeAsync().Result.Username;
        }

        async Task HandleUpdateMessageAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;
            if (!dialogMode.ContainsKey(chatId))
            {
                dialogMode.Add(chatId,ChatMode.CHATTING);
            }
            var username = message.Chat.FirstName;
            if (message.Type == MessageType.Text)
            {
                var messageText = update.Message.Text.Replace("ё","е");

                Console.WriteLine($"Received a '{messageText}' message in chat {chatId} with {username}.");
                if (messageText == "/bars")
                {
                    dialogMode[chatId] = ChatMode.CHOOSING;
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Ха-ха, ты попал на заглушку баров!",
                        cancellationToken: cancellationToken);
                    return;
                }
                if (messageText == "/morse")
                {
                    lastRecognizedLetter = "none";
                    dialogMode[chatId] = ChatMode.RECOGNIZING;
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Скинь фоточку одной из этих букв (азбукой Морзе, само собой): А, Г, Е, З, Н, П, Т, Ц, Ш, Ь.\nЕсли что, я распознаю с точностью ~90%, не обижайся, если я ошибусь :(",
                        cancellationToken: cancellationToken);
                    return;
                }
                
                if (dialogMode[chatId] == ChatMode.CHATTING)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: aimlService.Talk(chatId, username, messageText),
                        cancellationToken: cancellationToken);
                    return;
                }

                if (dialogMode[chatId] == ChatMode.RECOGNIZING)
                {
                    if (messageText.ToLower() == "хватит")
                    {
                        lastRecognizedLetter = "none";
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Окей, тогда давай болтать!",
                            cancellationToken: cancellationToken);
                        dialogMode[chatId] = ChatMode.CHATTING;
                        return;
                    }
                    if (lastRecognizedLetter!="none")
                    {
                        string answer;
                        if (messageText.Length > 1)
                        {
                            answer = "Ну я вообще то букву ждал... Ну, будем считать что это было правильно";
                        }
                        else if (messageText.ToUpper() == lastRecognizedLetter)
                        {
                            answer = aimlService.Talk(chatId, username, "угадал");
                        }
                        else
                        {
                            answer = aimlService.Talk(chatId, username, "промахнулся");
                            
                        }
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: answer,
                            cancellationToken: cancellationToken);
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Если хочешь болтать дальше, напиши \"хватит\", ну или продолжим играть в угадайку",
                            cancellationToken: cancellationToken);
                        
                        lastRecognizedLetter = "none";
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Мы тут болтать будем, или всё-таки морзе распознавать?",
                            cancellationToken: cancellationToken);
                    }
                }
                
            }
            // Загрузка изображений пригодится для соединения с нейросетью
            if (message.Type == MessageType.Photo)
            {
                if (dialogMode[chatId] != ChatMode.RECOGNIZING)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Фотка красивая, но мы же сейчас не в распознавание букв играем, не?",
                        cancellationToken: cancellationToken);
                    return;
                }
                var photoId = message.Photo.Last().FileId;
                Telegram.Bot.Types.File fl = await client.GetFileAsync(photoId, cancellationToken: cancellationToken);
                var imageStream = new MemoryStream();
                await client.DownloadFileAsync(fl.FilePath, imageStream, cancellationToken: cancellationToken);
                var img = new Bitmap(Image.FromStream(imageStream));
                var predicted = networkService.predict(img);
                
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: aimlService.Talk(chatId, username, $"предсказываю {predicted}"),
                    cancellationToken: cancellationToken);
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: aimlService.Talk(chatId, username, $"жду правду"),
                    cancellationToken: cancellationToken);
                lastRecognizedLetter = predicted;
                // Но вместо этого пошлём картинку назад
                // Стрим помнит последнее место записи, мы же хотим теперь прочитать с самого начала
                // imageStream.Seek(0, 0);
                // await client.SendPhotoAsync(
                //     message.Chat.Id,
                //     imageStream,
                //     "Пока что я не знаю, что делать с картинками, так что держи обратно",
                //     cancellationToken: cancellationToken
                // );
                return;
            }
            // Можно обрабатывать разные виды сообщений, просто для примера пробросим реакцию на них в AIML
            if (message.Type == MessageType.Video)
            {
                await client.SendTextMessageAsync(message.Chat.Id, aimlService.Talk(chatId, username, "Видео"), cancellationToken: cancellationToken);
                return;
            }
            if (message.Type == MessageType.Audio)
            {
                await client.SendTextMessageAsync(message.Chat.Id, aimlService.Talk(chatId, username, "Аудио"), cancellationToken: cancellationToken);
                return;
            }
        }

        Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var apiRequestException = exception as ApiRequestException;
            if (apiRequestException != null)
                Console.WriteLine($"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}");
            else
                Console.WriteLine(exception.ToString());
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // Заканчиваем работу - корректно отменяем задачи в других потоках
            // Отменяем токен - завершатся все асинхронные таски
            cts.Cancel();
        }
    }
}
