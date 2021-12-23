using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using CLIPSNET;

namespace AIMLTGBot
{
    public class CLIPSService
    {
        private CLIPSNET.Environment clips = new CLIPSNET.Environment();
        private SpeechSynthesizer roboWoman;

        Dictionary<int, Fact> facts;
        Dictionary<int, Rule> rules;
        private string clipsCode = "";
        private List<KeyValuePair<string, double>> finalChoice;
        private string currentQuestion = "";
        private List<int> currentFactOptions;
        private int processedRules = 0;

        public CLIPSService()
        {
            roboWoman = new SpeechSynthesizer();
            roboWoman.SetOutputToDefaultAudioDevice();
            roboWoman.SelectVoice("Microsoft Irina Desktop");
            roboWoman.Rate = 4;
            facts = new Dictionary<int, Fact>();
            rules = new Dictionary<int, Rule>();
            finalChoice = new List<KeyValuePair<string, double>>();
            currentFactOptions = new List<int>();
            loadDB();
            clipsCode = generateCLIPScode();
            clips.Clear();
            clips.LoadFromString(clipsCode);
            clips.Reset();
        }

        InitialFactType parseType(String type)
        {
            switch (type)
            {
                case "fia": return InitialFactType.DRINK_TYPE;
                case "fib": return InitialFactType.BUDGET;
                case "fic": return InitialFactType.COMPANY_SIZE;
                case "fil": return InitialFactType.LOCATION;
                case "fi": return InitialFactType.FEATURE;
                case "fn": return InitialFactType.OPPOSITE_FEATURE;
                default: throw new Exception("Типы фактов всё сломали :(");
            }
        }

        void loadDB()
        {
            using (var sr = new StreamReader("./db/db.txt"))
            {
                while (!sr.EndOfStream)
                {
                    var data = sr.ReadLine().Split(';');
                    var id = data[0].Split('-');
                    if (id[0].Equals("f"))
                    {
                        facts.Add(int.Parse(id[1]), new Fact(data[1]));
                    }
                    else if (id[0].StartsWith("f"))
                    {
                        if (id[0].Equals("ff"))
                        {
                            facts.Add(int.Parse(id[1]), new FiniteFact(data[1]));
                            continue;
                        }

                        var fact = new InitialFact(data[1], parseType(id[0]));
                        if (fact.factType == InitialFactType.OPPOSITE_FEATURE)
                        {
                            (facts[int.Parse(id[2])] as InitialFact).oppositeFact = int.Parse(id[1]);
                            facts.Add(int.Parse(id[1]), fact);
                            continue;
                        }

                        facts.Add(int.Parse(id[1]), fact);
                    }
                    else if (data[0].StartsWith("r"))
                    {
                        if (data.Count() != 5)
                        {
                            throw new ArgumentException("В правиле поплыла структура");
                        }

                        var premises = data[1].Split(',').Select(x => int.Parse(x.Split('-')[1])).ToList();
                        rules.Add(int.Parse(id[1]),
                            new Rule(premises, int.Parse(data[2].Split('-')[1]), data[3],
                                processRuleCertainty(data[4])));
                    }
                    else
                    {
                        throw new Exception("Something went wrong");
                    }
                }
            }
        }

        double processRuleCertainty(string input)
        {
            var original = double.Parse(input, CultureInfo.InvariantCulture);
            var scaled = (2 - original) * original; // Увеличиваем на (1 - x)%
            if (scaled > 1)
            {
                return 1;
            }

            return scaled;
        }

        private string generateCLIPScode()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(File.ReadAllText("./db/base.clp"));
            foreach (var rule in rules)
            {
                sb.AppendLine($"(defrule r-{rule.Key}");
                if (facts[rule.Value.conclusion] is FiniteFact)
                {
                    sb.AppendLine("(declare (salience 39))");
                }
                else
                {
                    sb.AppendLine("(declare (salience 40))");
                }

                StringBuilder sbCertainty = new StringBuilder();
                for (var i = 0; i < rule.Value.premises.Count; i++)
                {
                    sb.AppendLine($"(fact (num {rule.Value.premises[i]})(description ?desc{i})(certainty ?cert{i}))");
                    sbCertainty.Append($"?cert{i} ");
                }

                sb.AppendLine("=>");
                sb.AppendLine(
                    $"(bind ?rule-cert (* (min {sbCertainty.ToString()}) {rule.Value.ruleCertainty.ToString(CultureInfo.InvariantCulture)}))");
                sb.AppendLine($"(bind ?ex-fact (nth$ 1 (find-fact ((?f fact))(eq ?f:num {rule.Value.conclusion}))))");
                sb.AppendLine($"(if (neq ?ex-fact nil)");
                sb.AppendLine(
                    "then (bind ?ex-cert (fact-slot-value ?ex-fact certainty)) (modify ?ex-fact (certainty (-(+ ?ex-cert ?rule-cert)(* ?ex-cert ?rule-cert))))");
                sb.AppendLine(
                    $"else (assert (fact (num {rule.Value.conclusion})(description \"{facts[rule.Value.conclusion].factDescription}\")(certainty ?rule-cert)))\n)");
                if (facts[rule.Value.conclusion] is FiniteFact)
                {
                    sb.Append($"(assert (appendmessagehalt (str-cat\"#");
                }
                else
                {
                    sb.Append($"(assert (appendmessagehalt (str-cat\"");
                }

                sb.AppendLine($"[Применили правило #{rule.Key}:");
                for (int i = 0; i < rule.Value.premises.Count; i++)
                {
                    sb.AppendLine($"/f-{rule.Value.premises[i]}: \" ?desc{i} \" [~\" ?cert{i} \"]/");
                }

                sb.AppendLine(
                    $"=> \n /f-{rule.Value.conclusion}: |{facts[rule.Value.conclusion].factDescription}| [|\" ?rule-cert \"|]/,\n или, если по человечески: {rule.Value.comment}]\")))");
                sb.AppendLine(")");
                sb.AppendLine("");
            }

            return sb.ToString();
        }

        private string HandleResponse()
        {
            //  Вытаскиаваем факт из ЭС
            String evalStr = "(find-fact ((?f ioproxy)) TRUE)";
            FactAddressValue fv = (FactAddressValue) ((MultifieldValue) clips.Eval(evalStr))[0];

            MultifieldValue damf = (MultifieldValue) fv["messages"];
            MultifieldValue vamf = (MultifieldValue) fv["answers"];
            if (damf.Count == 0)
            {
                return "end";
            }

            //outputBox.Text += "Новая итерация : " + System.Environment.NewLine;
            for (int i = 0; i < damf.Count; i++)
            {
                LexemeValue da = (LexemeValue) damf[i];
                byte[] bytes = Encoding.Default.GetBytes(da.Value);
                string message = Encoding.UTF8.GetString(bytes);
                if (message.StartsWith("#ask"))
                {
                    var phrases = new List<string>();
                    if (vamf.Count > 0)
                    {
                        for (int j = 0; j < vamf.Count; j++)
                        {
                            //  Варианты !!!!!
                            LexemeValue va = (LexemeValue) vamf[j];
                            byte[] bytess = Encoding.Default.GetBytes(va.Value);
                            string messagee = Encoding.UTF8.GetString(bytess);
                            phrases.Add(messagee);
                        }
                    }

                    processedRules++;
                    clips.Eval("(assert (clearmessage))");
                    return askSomeQuestion(message, phrases);
                }

                if (message.StartsWith("#"))
                {
                    var parts = message.Split('|');
                    finalChoice.Add(new KeyValuePair<string, double>(parts[1],
                        double.Parse(parts[3], CultureInfo.InvariantCulture)));
                }
            }
            
            clips.Eval("(assert (clearmessage))");
            processedRules++;
            return "next";
        }

        public string startOutput()
        {
            processedRules = 0;
            clips.Reset();
            clips.Run();
            string answer;
            while ((answer = HandleResponse()) == "next")
            {
                clips.Run();
            }
            return answer;
        }
        
        string askSomeQuestion(string message, List<string> answers)
        {
            currentQuestion = message;
            currentFactOptions.Clear();
            StringBuilder sb = new StringBuilder();
            if (message.EndsWith("features"))
            {
                sb.AppendLine("Для начала давай определимся с фичами бара твоей мечты! Выбери не больше 3:");
                for (var index = 0; index < answers.Count; index++)
                {
                    var answer = answers[index];
                    var splitted = answer.Split('-');
                    currentFactOptions.Add(int.Parse(splitted[0]));
                    sb.AppendLine($"{index + 1}) {splitted[1]}");
                }
                sb.AppendLine("В ответ пришли номера наиболее подходящих вариантов через пробел. Ну, или если тебе всё равно, пришли 0.");
            }
            else if (message.EndsWith("location"))
            {
                sb.AppendLine("А какой район тебе лучше подходит? Оцени каждый от 0 до 100:");
                for (var index = 0; index < answers.Count; index++)
                {
                    var answer = answers[index];
                    var splitted = answer.Split('-');
                    currentFactOptions.Add(int.Parse(splitted[0]));
                    sb.AppendLine($"{index + 1}) {splitted[1]}");
                }
                sb.AppendLine("В ответ пришли через пробел 3 оценки - соответственно каждому району по порядку.");
            }
            else if (message.EndsWith("company"))
            {
                sb.AppendLine("А ты тусовщик или одиночка? Выбери подходящие варианты:");
                for (var index = 0; index < answers.Count; index++)
                {
                    var answer = answers[index];
                    var splitted = answer.Split('-');
                    currentFactOptions.Add(int.Parse(splitted[0]));
                    sb.AppendLine($"{index + 1}) {splitted[1]}");
                }
                sb.AppendLine("В ответ пришли через пробел номера, соответствующие размеру компании - можно выбрать один или несколько вариантов.");
            }
            else if (message.EndsWith("budget"))
            {
                sb.AppendLine("Проверь кошелёк перед тем как пить! Трезво оцени свои финансовые возможности от 0 до 100:");
                for (var index = 0; index < answers.Count; index++)
                {
                    var answer = answers[index];
                    var splitted = answer.Split('-');
                    currentFactOptions.Add(int.Parse(splitted[0]));
                    sb.AppendLine($"{index + 1}) {splitted[1]}");
                }
                sb.AppendLine("В ответ пришли через пробел 3 оценки - соответственно каждому варианту по порядку.");
            }
            else if (message.EndsWith("drinks"))
            {
                sb.AppendLine("А теперь самое главное! Что пить-то будем? Выбери из списка:");
                for (var index = 0; index < answers.Count; index++)
                {
                    var answer = answers[index];
                    var splitted = answer.Split('-');
                    currentFactOptions.Add(int.Parse(splitted[0]));
                    sb.AppendLine($"{index + 1}) {splitted[1]}");
                }
                sb.AppendLine("В ответ пришли через пробел номера, соответствующие тому, что хочешь выпить - можно выбрать один или несколько вариантов.");
            }
            else
            {
                throw new Exception("Упс... Кажется, я сломал клипс...");
            }

            return sb.ToString();
        }

        public void onQuestionAnswered(string answer)
        {
            if (currentQuestion.EndsWith("features"))
            {
                if (answer == "0")
                {
                    return;
                }
                var selected = answer.Split(' ').Select(int.Parse).Select(x => currentFactOptions[x-1]).ToList();
                foreach (var t in currentFactOptions)
                {
                    if (!selected.Contains(t))
                    {
                        selected.Add((facts[t] as InitialFact).oppositeFact);
                    }
                }

                var selectedFacts = selected.Select(x => facts.GetEntry(x)).ToList();
                foreach (var fact in selectedFacts)
                {
                    fact.Value.certainty = 1;
                }
                setQuestionAnswer(selectedFacts);
                return;
            }

            if (currentQuestion.EndsWith("drinks") || currentQuestion.EndsWith("company"))
            {
                var selected = answer.Split(' ').Select(int.Parse).Select(x => currentFactOptions[x-1]).Select(x => facts.GetEntry(x)).ToList();
                foreach (var fact in selected)
                {
                    fact.Value.certainty = 1;
                }
                setQuestionAnswer(selected);
                return;
            }
            
            if (currentQuestion.EndsWith("budget") || currentQuestion.EndsWith("location"))
            {
                var certainties = answer.Split(' ').Select(x => int.Parse(x)/100.0).ToArray();
                List<KeyValuePair<int, Fact>> selected = new List<KeyValuePair<int, Fact>>();
                for (int i = 0; i < currentFactOptions.Count; i++)
                {
                    var fact = facts.GetEntry(currentFactOptions[i]);
                    fact.Value.certainty = certainties[i];
                    if (certainties[i] > 0.01)
                    {
                        selected.Add(fact);
                    }
                }
                setQuestionAnswer(selected);
                return;
            }

            throw new Exception("Сюрприз! Ты где-то накосячил");
        }

        public string nextIteration()
        {
            string answer;
            clips.Run();
            while ((answer = HandleResponse()) == "next")
            {
                clips.Run();
            }
            return answer;
        }

        public string getTextResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Окей. Я (вместе с Ириной! Она должна была тебе рассказать всё это, но простыла, бедняжка) проанализировали кучу всего и перебрали целых {processedRules} правил. Вот наиболее подходящие тебе бары:");
            foreach (var bar in finalChoice.OrderByDescending(x=>x.Value))
            {
                sb.AppendLine($"{bar.Key} - {Math.Round(bar.Value * 100,2)}%");
            }
            sb.AppendLine("Надеюсь, ты хорошо проведёшь вечер!");
            return sb.ToString();
        }

        public MemoryStream getAudioResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Ой. Кажется, я не представилась. Меня зовут Ирина, и всё это время ты общался со мной. Я перебрала целых {processedRules} правил, и вот что у меня получилось:");
            foreach (var bar in finalChoice.OrderByDescending(x=> x.Value))
            {
                sb.AppendLine($"С уверенностью {Math.Round(bar.Value * 100, 2)}% тебе подойдёт {bar.Key}");
            }

            sb.AppendLine("Надеюсь, ты хорошо проведёшь вечер! Пока!");
            MemoryStream audio = new MemoryStream();
            roboWoman.SetOutputToWaveStream(audio);
            roboWoman.Speak(sb.ToString());
            return audio;
        }
        
        void setQuestionAnswer(List<KeyValuePair<int, Fact>> selectedFacts){
            foreach (var pair in selectedFacts)
            {
                clips.Eval($"(assert (fact (num {pair.Key})(description \"{pair.Value.factDescription}\")(certainty {Math.Round(pair.Value.certainty, 2).ToString(CultureInfo.InvariantCulture)})))");
            }
        }
    }
}