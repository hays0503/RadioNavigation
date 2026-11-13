using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Pure text view of frozen observations, not another navigation engine.
    /// Missing legacy inputs stay missing. No live clock, preferences or ephemeris reads.</summary>
    internal static class SightBookText
    {
        internal const string UtcFormat = "yyyy-MM-dd HH:mm:ss.fffffff";
        internal static string Compose(SextantSaveSnapshot state, string fixSteps)
        {
            var pages = new List<string>();
            pages.Add(Introduction);
            AddObservation(pages, state.Observation1, 1, state.CalibrationMinutes);
            AddObservation(pages, state.Observation2, 2, state.CalibrationMinutes);
            AddObservation(pages, state.Observation3, 3, state.CalibrationMinutes);
            if (state.Observation1 == null)
                pages.Add("НОВАЯ СЕРИЯ\nНаблюдений нет. Наведи прибор и запиши успешный замер клавишей I.\n");
            else if (state.Observation2 == null)
                pages.Add("ОПРЕДЕЛЁННОЕ МЕСТО ЕЩЁ НЕ ПОЛУЧЕНО\nОдна линия положения не задаёт точку. Нужен второй замер (I).\n");
            else
                pages.Add(FixPage(state, fixSteps));
            pages.Add(References);
            var text = new StringBuilder(8192);
            text.Append("# RADIONAV SIGHT BOOK — текущая серия, text v1\n");
            text.Append("# Числа заморожены при записи; не журнал похода.\n\n");
            for (int i = 0; i < pages.Count; i++)
            {
                text.Append("=====Начало=====\nКНИГА РАСЧЁТОВ · стр. ")
                    .Append(i + 1).Append('/').Append(pages.Count).Append("\n\n")
                    .Append(pages[i]).Append("\n=====Конец=====\n\n");
            }
            return text.ToString();
        }

        private static void AddObservation(List<string> pages, SextantObservation obs, int index, double calibrationMinutes)
        {
            if (obs == null)
                return;
            var sb = new StringBuilder(4096);
            sb.Append("НАБЛЮДЕНИЕ №").Append(index).Append(" — ").Append(obs.ToBody().DisplayName).Append('\n');
            if (obs.BodyKind == CelestialBodyKind.Star)
                sb.Append("HIP(номер звезды в каталоге) = ").Append(obs.BodyHip).Append('\n');
            sb.Append("UT(всемирное время) = ").Append(obs.Utc.ToString(UtcFormat, CultureInfo.InvariantCulture)).Append("\n\n");
            AppendReduction(sb, obs);
            AppendAlmanac(sb, obs, calibrationMinutes);
            AppendLocalAngle(sb, obs);
            AppendHeightAzimuth(sb, obs);
            AppendIntercept(sb, obs);
            AppendLine(sb, obs);
            pages.Add(sb.ToString());
        }

        private static void AppendReduction(StringBuilder sb, SextantObservation obs)
        {
            if (obs.SolarReductionVersion == 1)
            {
                Card(sb, "1", "КАКАЯ ВЫСОТА ПОСЛЕ ПОПРАВКИ НУЛЯ?",
                    "Hs(отсчёт барабана, до поправок) = " + Angle(obs.RawHsDeg) +
                    "; ИП(поправка нуля) = " + Number(obs.IndexErrorArcMin) + "′",
                    "Прибавь поправку нуля в угловых минутах к отсчёту.",
                    "h(высота после поправки нуля) = " + Angle(obs.IndexCorrectedHDeg),
                    "Поправку нуля внесли один раз. В десятичных градусах: h = Hs + ИП / 60.", "2a");
                AppendSolarCorrection(sb, obs);
                Card(sb, "2", "КАКАЯ ИСПРАВЛЕННАЯ ВЫСОТА?",
                    "h(высота после поправки нуля) = " + Angle(obs.IndexCorrectedHDeg) +
                    "; F VIII(полная поправка нижнего края) = " + Number(obs.SolarFviiiArcMin, "F4") + "′",
                    "Прибавь подписанную табличную поправку. Отрицательную — вычти.",
                    "Ho(исправленная высота центра) = " + Angle(obs.HoDeg),
                    "Полудиаметр, наклон горизонта и рефракцию повторно не добавляй.", "3a");
                return;
            }
            Card(sb, "1", "ЧТО СОХРАНЕНО О ПОПРАВКЕ НУЛЯ?",
                "ИП(поправка нуля) = " + Number(obs.IndexErrorArcMin) + "′",
                "Прочитай сохранённую запись; сырой отсчёт для этой записи не сохранён.",
                "Hs(отсчёт барабана, до поправок) = нет данных",
                "Не восстанавливай отсчёт вычитанием поправки: исходное значение могло быть квантовано.", "2");
            string model = obs.BodyKind == CelestialBodyKind.Sun
                ? "Старое Солнце: модель редукции неизвестна. F VIII задним числом не применять."
                : "В действующей модели мода внесена поправка нуля и квантование; F VIII, рефракция и наклон горизонта не добавляются.";
            Card(sb, "2", "КАКУЮ ВЫСОТУ ИСПОЛЬЗОВАЛ РАСЧЁТ?",
                "Ho(сохранённая измеренная высота) = " + Angle(obs.HoDeg),
                model,
                "Ho(высота для сравнения) = " + Angle(obs.HoDeg),
                obs.BodyKind == CelestialBodyKind.Moon
                    ? "Луна: измеряется центр диска; параллакс учтён в направлении светила. Это модель мода, не бумажная лунная редукция."
                    : "Значение уже содержит принятую при записи инструментальную поправку; повторно её не вносить.", "3a");
        }

        private static void AppendSolarCorrection(StringBuilder sb, SextantObservation obs)
        {
            SolarCorrectionDetails d;
            if (!SolarHeightReduction.TryDetails(obs.IndexCorrectedHDeg, obs.SolarEyeHeightM, obs.Utc.Month, out d))
                return;
            Card(sb, "2a", "КАКИЕ ВХОДЫ ДЛЯ F VIII?",
                "h(высота после поправки нуля) = " + Angle(obs.IndexCorrectedHDeg) +
                "; глаз(высота глаза) = " + Number(obs.SolarEyeHeightM, "F6") + " м; месяц = " + obs.Utc.Month,
                "Открой F VIII: высота задаёт строки, высота глаза — столбцы.",
                "Строки: " + Angle(d.LowAltitudeDeg) + " и " + Angle(d.HighAltitudeDeg),
                "F VIII: печатная стр. 72 / PDF 98; клетки ниже — из той же таблицы, что использует мод.", "2b");
            Card(sb, "2b", "ГДЕ МЕЖДУ СТОЛБЦАМИ?",
                "глаз(высота глаза) = " + Number(obs.SolarEyeHeightM, "F6") + " м; нижний столбец = " + d.LowEyeM + " м",
                "Вычти высоту нижнего столбца; шаг столбцов — один метр.",
                "w(доля шага по высоте глаза) = " + Number(d.EyeWeight, "F4"),
                "Доля от нуля до единицы; верхний столбец = нижний + 1 м.", "2c");
            Card(sb, "2c", "ПОПРАВКА В НИЖНЕЙ СТРОКЕ?",
                "нижняя левая клетка = " + Number(d.LowLeftArcMin) + "′; правая = " + Number(d.LowRightArcMin) +
                "′; w(доля шага глаза) = " + Number(d.EyeWeight, "F4"),
                "Левая + w × (правая − левая). Клетки уже переведены из десятых минуты в минуты.",
                "c₀(поправка нижней строки) = " + Number(d.LowArcMin, "F4") + "′",
                "Результат лежит между двумя клетками.", "2d");
            Card(sb, "2d", "ПОПРАВКА В ВЕРХНЕЙ СТРОКЕ?",
                "верхняя левая клетка = " + Number(d.HighLeftArcMin) + "′; правая = " + Number(d.HighRightArcMin) +
                "′; w(доля шага глаза) = " + Number(d.EyeWeight, "F4"),
                "Левая + w × (правая − левая).",
                "c₁(поправка верхней строки) = " + Number(d.HighArcMin, "F4") + "′",
                "Результат лежит между двумя клетками.", "2e");
            Card(sb, "2e", "ГДЕ МЕЖДУ СТРОКАМИ?",
                "h(высота) = " + Angle(obs.IndexCorrectedHDeg) + "; нижняя строка = " + Angle(d.LowAltitudeDeg) +
                "; верхняя строка = " + Angle(d.HighAltitudeDeg),
                "Разность между высотой и нижней строкой раздели на шаг между строками.",
                "v(доля шага по высоте светила) = " + Number(d.AltitudeWeight, "F6"),
                "Перед делением все три угла должны быть в одной единице.", "2f");
            Card(sb, "2f", "КАКАЯ БАЗОВАЯ ПОПРАВКА?",
                "c₀(нижняя строка) = " + Number(d.LowArcMin, "F4") + "′; c₁(верхняя строка) = " + Number(d.HighArcMin, "F4") +
                "′; v(доля шага высоты) = " + Number(d.AltitudeWeight, "F6"),
                "c₀ + v × (c₁ − c₀).",
                "b(базовая поправка) = " + Number(d.BaseArcMin, "F4") + "′",
                "Округляй только запись результата, не промежуточный расчёт.", "2g");
            Card(sb, "2g", "КАК УЧЕСТЬ МЕСЯЦ?",
                "b(базовая поправка) = " + Number(d.BaseArcMin, "F4") + "′; m(поправка месяца) = " + Number(d.MonthArcMin) + "′",
                "Сложи подписанные поправки.",
                "F VIII(сохранённая полная поправка) = " + Number(obs.SolarFviiiArcMin, "F4") + "′",
                "Табличная сумма = " + Number(d.BaseArcMin + d.MonthArcMin, "F4") + "′. Это сверка с таблицей текущей версии, не замена записи.", "2");
        }

        private static void AppendAlmanac(StringBuilder sb, SextantObservation obs, double calibrationMinutes)
        {
            if (obs.SolarReductionVersion == 1)
                AppendSolarAlmanac(sb, obs);
            else
                AppendBodyAlmanac(sb, obs, calibrationMinutes);
        }

        private static void AppendSolarAlmanac(StringBuilder sb, SextantObservation obs)
        {
            Sh5AlmanacDetails d;
            if (Sh5SunAlmanac.TryDetails(obs.Utc, out d))
            {
                Card(sb, "3a", "КАКУЮ СТРОКУ АЛЬМАНАХА ОТКРЫТЬ?",
                    "UT(всемирное время) = " + obs.Utc.ToString(UtcFormat, CultureInfo.InvariantCulture),
                    "Возьми предшествующую четырёхчасовую строку SH5.",
                    "Строка = " + d.RowUtc.ToString("HH:mm", CultureInfo.InvariantCulture) +
                    "; τ(время после строки) = " + Number(d.Hours, "F6") + " часа",
                    "Часы включают минуты / 60 и секунды / 3600; не десятичную запись циферблата.", "3b");
                Card(sb, "3b", "КАКОЙ ГРИНВИЧСКИЙ ЧАСОВОЙ УГОЛ?",
                    "GHA₀(угол строки) = " + Angle(d.GhaDeg) + "; τ(время после строки) = " + Number(d.Hours, "F6") + " часа",
                    "Прибавь 15° за каждый час; результат приведи к диапазону 0–360°.",
                    "GHA(сохранённый гринвичский часовой угол) = " + Angle(obs.GhaBodyDeg),
                    "Шаг 15°/час относится к солнечной таблице SH5, не к Луне или звёздам.", "3c");
                Card(sb, "3c", "КАКОЕ СКЛОНЕНИЕ?",
                    "δ₀(склонение строки) = " + SignedAngle(d.DecDeg) + "; d(изменение за четыре часа) = " +
                    Number(d.IncrementArcMin) + "′; τ(время после строки) = " + Number(d.Hours, "F6") + " часа",
                    "К склонению строки прибавь d × τ / 4, сохранив знак; минуты переведи в градусы только при необходимости.",
                    "δ(сохранённое склонение) = " + SignedAngle(obs.DecDeg),
                    "Строки — из проверенного печатного пакета. После загрузки старой записи это сверка, не пересчёт наблюдения.", "4a");
            }
            else
                Card(sb, "3a", "ГДЕ СОЛНЦЕ В СОХРАНЁННОЙ ЗАПИСИ?", "Печатный пакет SH5 сейчас недоступен.",
                    "Прочитай замороженные значения; не подменяй их современной эфемеридой.",
                    "GHA(гринвичский часовой угол) = " + Angle(obs.GhaBodyDeg) + "; δ(склонение) = " + SignedAngle(obs.DecDeg),
                    "Подробная строка альманаха недоступна.", "4a");
        }

        private static void AppendBodyAlmanac(StringBuilder sb, SextantObservation obs, double calibrationMinutes)
        {
            string source = AlmanacSource(obs.BodyKind);
            Card(sb, "3a", "ОТКУДА ПОЛУЧЕНО НАПРАВЛЕНИЕ СВЕТИЛА?",
                "UT(время наблюдения) = " + obs.Utc.ToString(UtcFormat, CultureInfo.InvariantCulture) +
                "; Δt(применённая калибровка времени серии) = " + Number(CelestialEphemeris.KindUsesCalibration(obs.BodyKind) ? calibrationMinutes : 0, "F3") + " мин",
                source,
                "δ(сохранённое склонение) = " + SignedAngle(obs.DecDeg),
                "Это вычисленная эфемерида мода, а не отсчёт из печатной F-Tafel.", "3b");
            Card(sb, "3b", "КАКОЙ УГОЛ ОТ ГРИНВИЧА?",
                "γ(гринвичский угол точки Овна) = " + Angle(obs.GhaAriesDeg) +
                "; SHA(угол светила от точки Овна) = " + Angle(obs.ShaBodyDeg),
                "Сложи углы; отбрось полные обороты по 360°.",
                "GHA(гринвичский часовой угол светила) = " + Angle(obs.GhaBodyDeg),
                "SHA здесь — сохранённый угол именно этого светила, включая лунное направление для наблюдателя.", "4a");
        }

        private static string AlmanacSource(CelestialBodyKind kind)
        {
            switch (kind)
            {
                case CelestialBodyKind.Moon:
                    return "Луна: направление рассчитано топоцентрически, для места наблюдателя; параллакс уже учтён.";
                case CelestialBodyKind.Star:
                    return "Звезда: каталог эпохи 1926.0 с прецессией к дате; калибровка времени серии не применяется.";
                default:
                    return "Старое Солнце: сохранённая эфемерида, не новая редукция SH5.";
            }
        }

        private static void AppendLocalAngle(StringBuilder sb, SextantObservation obs)
        {
            Card(sb, "4a", "ИЗ КАКОЙ ТОЧКИ ВЕДЁТСЯ РАСЧЁТ?",
                "φ(широта расчёта) = " + Coordinate(obs.DrLatDeg, true) + "; λ(долгота расчёта) = " + Coordinate(obs.DrLonDeg, false),
                "Отметь эту точку на карте. В текущем моде она взята у поставщика игровой позиции, не из ручного счисления.",
                "Точка расчёта = " + Coordinate(obs.DrLatDeg, true) + ", " + Coordinate(obs.DrLonDeg, false),
                "Не заменяй её удобной табличной точкой без нового расчёта высоты и азимута.", "4b");
            Card(sb, "4b", "КАКОЙ МЕСТНЫЙ ЧАСОВОЙ УГОЛ?",
                "GHA(гринвичский угол) = " + Angle(obs.GhaBodyDeg) + "; λ(долгота) = " + Coordinate(obs.DrLonDeg, false),
                "К GHA прибавь восточную долготу, западную вычти. Убери полные обороты.",
                "LHA(местный часовой угол) = " + Angle(obs.LhaDeg),
                "Диапазон 0–360°; знак долготы не означает направление азимута.", "4c");
            double t = obs.LhaDeg <= 180 ? obs.LhaDeg : 360 - obs.LhaDeg;
            Card(sb, "4c", "КАКОЙ МЕНЬШИЙ ЧАСОВОЙ УГОЛ?",
                "LHA(местный часовой угол) = " + Angle(obs.LhaDeg),
                "До 180° возьми LHA; после 180° вычти LHA из 360°.",
                "t(меньший часовой угол) = " + Angle(t),
                "До 180° светило западнее меридиана, после — восточнее; на границах оно на меридиане.", "5");
        }

        private static void AppendHeightAzimuth(StringBuilder sb, SextantObservation obs)
        {
            Card(sb, "5", "КАКОЙ БЫЛА БЫ ВЫСОТА В ТОЧКЕ РАСЧЁТА?",
                "φ(широта расчёта) = " + Coordinate(obs.DrLatDeg, true) + "; δ(склонение) = " + SignedAngle(obs.DecDeg) +
                "; LHA(местный угол) = " + Angle(obs.LhaDeg),
                "В бумажном методе эти входы ведут в таблицы высоты. Мод использовал сферический расчёт; его результат ниже заморожен.",
                "h1(вычисленная высота, Hc) = " + Angle(obs.HcDeg),
                "F I/F II не оцифрованы. Вымышленных клеток U/V и обратного поиска здесь нет.", "5a");
            Card(sb, "5a", "В КАКУЮ СТОРОНУ НАХОДИЛОСЬ СВЕТИЛО?",
                "φ(широта расчёта) = " + Coordinate(obs.DrLatDeg, true) + "; δ(склонение) = " + SignedAngle(obs.DecDeg) +
                "; LHA(местный угол) = " + Angle(obs.LhaDeg),
                "Прочитай сохранённый истинный азимут, по часовой стрелке от севера.",
                "Az(истинный азимут) = " + Number(obs.AzDeg, "F3") + "°",
                "Север 0°, восток 90°, юг 180°, запад 270°. Это результат мода, не подбор ветви F XI.", "6a");
        }

        private static void AppendIntercept(StringBuilder sb, SextantObservation obs)
        {
            double difference = (obs.HoDeg - obs.HcDeg) * 60;
            Card(sb, "6a", "НА СКОЛЬКО ОТЛИЧАЮТСЯ ВЫСОТЫ?",
                "Ho(измеренная высота) = " + Angle(obs.HoDeg) + "; h1(вычисленная высота) = " + Angle(obs.HcDeg),
                "Вычти вычисленную высоту из измеренной. Разность запиши в угловых минутах.",
                "Δh(разность высот) = " + Number(difference, "F3") + "′",
                "Только для десятичных градусов: Δh = 60 × (Ho − h1). Готовые минуты ещё раз на 60 не умножай.", "6b");
            Card(sb, "6b", "КАКОЙ ИНТЕРЦЕПТ?",
                "Δh(разность высот) = " + Number(difference, "F3") + "′",
                "Одна угловая минута разности высот соответствует одной морской миле смещения линии.",
                "a(сохранённый интерцепт) = " + Number(obs.InterceptNm, "F3") + " морской мили",
                "Плюс — к светилу, минус — от светила. Большой интерцепт требует проверки наведения, но сам по себе не доказывает ошибку.", "7a");
        }

        private static void AppendLine(StringBuilder sb, SextantObservation obs)
        {
            double direction = RunningFixCalculator.Normalize360(obs.AzDeg + (obs.InterceptNm < 0 ? 180 : 0));
            Card(sb, "7a", "КУДА ОТЛОЖИТЬ СМЕЩЕНИЕ?",
                "a(интерцепт) = " + Number(obs.InterceptNm, "F3") + " морской мили; Az(азимут светила) = " + Number(obs.AzDeg, "F3") + "°",
                "При плюсе иди по азимуту, при минусе — в противоположную сторону. Отсчёт веди от точки расчёта карточки 4a.",
                "Направление смещения = " + Number(direction, "F3") + "°; расстояние = " + Number(Math.Abs(obs.InterceptNm), "F3") + " морской мили",
                "При нулевом интерцепте точка линии совпадает с точкой расчёта.", "7b");
            LineOfPosition lop = RunningFixCalculator.BuildLop(obs);
            Card(sb, "7b", "ГДЕ ПРОВЕСТИ ЛИНИЮ ПОЛОЖЕНИЯ?",
                "Точка расчёта = " + Coordinate(obs.DrLatDeg, true) + ", " + Coordinate(obs.DrLonDeg, false) +
                "; расстояние = " + Number(Math.Abs(obs.InterceptNm), "F3") + " морской мили; направление = " + Number(direction, "F3") + "°",
                "Отложи указанное смещение; через полученную точку проведи прямую поперёк направления на светило.",
                "P(точка линии) = " + Coordinate(lop.TangentLatDeg, true) + ", " + Coordinate(lop.TangentLonDeg, false),
                "Линия перпендикулярна Az(истинному азимуту) = " + Number(obs.AzDeg, "F3") + "°. Это местная касательная, не вся окружность равных высот.", "итог");
            sb.Append("ИТОГ НАБЛЮДЕНИЯ\nHo(измеренная высота) = ").Append(Angle(obs.HoDeg))
                .Append("\nh1(вычисленная высота) = ").Append(Angle(obs.HcDeg))
                .Append("\nAz(истинный азимут) = ").Append(Number(obs.AzDeg, "F3"))
                .Append("°\na(интерцепт) = ").Append(Number(obs.InterceptNm, "F3")).Append(" морской мили\n");
        }

        private static string FixPage(SextantSaveSnapshot state, string fixSteps)
        {
            var sb = new StringBuilder("ОПРЕДЕЛЁННОЕ МЕСТО · FIX\n\n");
            if (!string.IsNullOrEmpty(fixSteps))
                sb.Append(fixSteps);
            else
                sb.Append("Подробный след решения не сохранён в записи v3. Ни курс, ни пару, ни вершины не восстанавливаем догадкой.\n");
            if (state.FixPublished)
                sb.Append("\nЗАМОРОЖЕННЫЙ РЕЗУЛЬТАТ\nFIX(определённое место) = ")
                    .Append(Coordinate(state.FixLatDeg, true)).Append(", ").Append(Coordinate(state.FixLonDeg, false))
                    .Append("\nΔ(расстояние до места игры при публикации, самопроверка) = ")
                    .Append(Number(state.FixErrorNm, "F3")).Append(" морской мили\n");
            else
                sb.Append("\nFIX(определённое место) не опубликован.\n");
            sb.Append("\nИСХОДНАЯ ЗАМОРОЖЕННАЯ КАРТОЧКА\nСокращения здесь оставлены как в игре; пояснения — в карточках книги.\n")
                .Append(state.SectionText).Append('\n');
            return sb.ToString();
        }

        internal static void Card(StringBuilder sb, string id, string title, string input,
            string action, string result, string check, string next)
        {
            sb.Append('[').Append(id).Append("] ").Append(title).Append("\nВозьми: ").Append(input)
                .Append("\nСделай/Найди: ").Append(action).Append("\nЗапиши: ").Append(result)
                .Append("\nПроверь: ").Append(check).Append("\nДальше: ").Append(next).Append("\n\n");
        }

        internal static string Number(double value, string format = "F2")
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "нет данных" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        internal static string Angle(double value)
        {
            double minutes = Math.Round(Math.Abs(value) * 60, 3, MidpointRounding.AwayFromZero);
            int degrees = (int)(minutes / 60);
            return (value < 0 ? "−" : "") + degrees.ToString(CultureInfo.InvariantCulture) + "°" +
                (minutes - degrees * 60).ToString("00.000", CultureInfo.InvariantCulture) + "′";
        }

        private static string SignedAngle(double value)
        {
            return (value < 0 ? "" : "+") + Angle(value);
        }

        internal static string Coordinate(double value, bool latitude)
        {
            string hemisphere = value < 0 ? " W" : " E";
            if (latitude)
                hemisphere = value < 0 ? " S" : " N";
            return Angle(Math.Abs(value)) + hemisphere;
        }

        private const string Introduction =
            "КАК ЧИТАТЬ\nОдна карточка — один вопрос: Возьми → Сделай/Найди → Запиши → Проверь → Дальше.\n" +
            "Нужные значения повторяются рядом с действием; итог наблюдения — после построения линии.\n" +
            "Это текущая серия: Солнце, Луна, звёзды и смешанные наблюдения. Успешное I перезаписывает файл; O начинает пустую книгу.\n" +
            "Отклонённое I, кадры игры и открытие прибора файл не переписывают. При загрузке сейва книга заменяется его серией.\n\n" +
            "ГРАНИЦА ТОЧНОСТИ\nСохранённые числа — из наблюдений и опубликованного FIX, не из живого положения лодки.\n" +
            "Печатные F VIII и солнечный альманах SH5 реализованы; подробности ниже — сверка с пакетом текущей версии.\n" +
            "F I/F II/F XI не оцифрованы. Вместо вымышленных отсчётов книга честно показывает вычисленные h1 и Az.\n" +
            "Это не универсальный бумажный справочник и не утверждение об историческом использовании SH5.\n" +
            "Сохранение v3 не содержит сырого отсчёта Луны/звёзд и полного следа FIX. Пропуски подписаны.\n" +
            "Интерактивное листание и отдельный режим «Штурман» пока не реализованы.\n";

        private const string References =
            "СПРАВОЧНО · ПОПРАВКИ И ПРОВЕРКА\n\n" +
            "СОЛНЦЕ\nF VIII(полная поправка нижнего края) уже включает полудиаметр, наклон горизонта и рефракцию; месяц учитывается один раз.\n" +
            "Область таблицы: высота после поправки нуля 3–90°, глаз 0–14 м. Знак поправки может быть отрицательным.\n" +
            "Классическое пояснение, НЕ второй расчёт мода: высота центра = отсчёт + поправка нуля − наклон горизонта − рефракция + полудиаметр + параллакс.\n" +
            "Все слагаемые должны быть в одной угловой единице. Разложение полной F VIII на отдельные числовые поправки не сохранено.\n" +
            "Водный горизонт игры не следует автоматически заменять земной формулой наклона горизонта.\n\n" +
            "ЛУНА\nСнимается центр диска. Дополнительная поправка полудиаметра не вносится; это не утверждение, что у Луны нет размера.\n" +
            "Параллакс включён в топоцентрическую эфемериду. Калибровка времени серии применяется к Луне, но не к SH5-Солнцу или звёздам.\n" +
            "В модели этого пути нет добавочной рефракции и наклона горизонта. Оценка точности лунной модели около ±0.1° — не точность каждого замера.\n\n" +
            "ЗВЁЗДЫ\nКаталог эпохи 1926.0, прецессия к дате. Звезда — точечный источник; поправки полудиаметра нет.\n" +
            "Калибровка времени серии не применяется; добавочная рефракция и наклон горизонта в этом пути не вносятся.\n\n" +
            "ЛИНИИ И FIX\nПоложительный интерцепт — к светилу, отрицательный — от светила. Прямая перпендикулярна истинному азимуту.\n" +
            "Разновременные линии переносятся параллельно на ход лодки до общего момента. При изменявшемся ходе это приближение постоянным курсом и скоростью.\n" +
            "Угол пересечения менее 15° — предупреждение о слабой геометрии; запрет публикации в моде начинается ниже 1°, не 15°.\n" +
            "Треугольник: радиус — максимальное расстояние от центроида до вершины, не радиус описанной окружности.\n" +
            "При радиусе свыше 6 морских миль или вырожденном треугольнике расчёт может выбрать пару, а не центр.\n\n" +
            "СОВРЕМЕННАЯ ПРОВЕРКА, НЕ ПЕЧАТНАЯ ВЫБОРКА\n" +
            "φ(широта), δ(склонение), LHA(местный часовой угол):\n" +
            "sin(h1) = sin φ × sin δ + cos φ × cos δ × cos LHA\n" +
            "Az = atan2(−cos δ × sin LHA, sin δ × cos φ − cos δ × cos LHA × sin φ)\n" +
            "h1(вычисленная высота) = asin(выражение выше); Az(истинный азимут) привести к 0–360°.\n" +
            "Для программной проверки переведи градусы в радианы до sin/cos и обратно после asin/atan2.\n" +
            "a(интерцепт в морских милях) = 60 × (Ho − h1), только когда обе высоты в десятичных градусах.\n" +
            "Если разность уже в угловых минутах, её число и есть интерцепт в морских милях.\n";
    }
}
