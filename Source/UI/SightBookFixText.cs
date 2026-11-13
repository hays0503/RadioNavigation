using System;
using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Composed at publication, with the SAME solver result as the card.
    /// Text is held until next record/reset; never re-solved by export or load.</summary>
    internal static class SightBookFixText
    {
        internal static string ComposeTransfers(RunningFixCalculator calc, double course, double speed, bool fresh, bool includeThird)
        {
            SextantObservation last = calc.Observation2;
            if (includeThird && calc.Observation1.Utc > last.Utc)
                last = calc.Observation1;
            if (includeThird && calc.Observation3.Utc > last.Utc)
                last = calc.Observation3;
            var sb = new StringBuilder();
            sb.Append("Общий момент линий = ").Append(last.Utc.ToString(SightBookText.UtcFormat, CultureInfo.InvariantCulture)).Append(" UT\n");
            AppendTransfer(sb, calc.Observation1, 1, last.Utc, course, speed, "F2a");
            AppendTransfer(sb, calc.Observation2, 2, last.Utc, course, speed, includeThird ? "F3a" : "F4");
            if (includeThird)
                AppendTransfer(sb, calc.Observation3, 3, last.Utc, course, speed, "F4");
            if (!fresh)
                sb.Append("ВНИМАНИЕ: оценка курса/скорости устарела; перенос ненадёжен.\n");
            double crossing = RunningFixCalculator.CrossingAngleDeg(calc.Observation1, calc.Observation2);
            sb.Append("Угол пересечения исходных линий №1/№2 = ").Append(SightBookText.Number(crossing, "F3")).Append("°.\n");
            if (crossing < 15)
                sb.Append("Слабая геометрия усиливает ошибку; нужны более разнесённые направления или время.\n");
            return sb.ToString();
        }

        private static void AppendTransfer(StringBuilder sb, SextantObservation obs, int index,
            DateTime latest, double course, double speed, string next)
        {
            double hours = Math.Max(0, (latest - obs.Utc).TotalHours);
            double run = speed * hours;
            SightBookText.Card(sb, "F" + index + "a", "НА СКОЛЬКО ПЕРЕНЕСТИ ЛИНИЮ №" + index + "?",
                "Δt(время до общего момента) = " + SightBookText.Number(hours * 60, "F3") +
                " мин; V(скорость над грунтом) = " + SightBookText.Number(speed, "F3") + " узла",
                "Переведи минуты в часы и умножь на скорость. Один узел — одна морская миля в час.",
                "S(ход лодки для переноса) = " + SightBookText.Number(run, "F3") + " морской мили",
                "При одновременных замерах или стоящей лодке перенос нулевой.", "F" + index + "b");
            LineOfPosition original = RunningFixCalculator.BuildLop(obs);
            LineOfPosition transferred = RunningFixCalculator.TranslateLop(original, course, run);
            SightBookText.Card(sb, "F" + index + "b", "ГДЕ ЛИНИЯ №" + index + " В ОБЩИЙ МОМЕНТ?",
                "K(курс над грунтом) = " + SightBookText.Number(course, "F3") +
                "°; S(ход лодки) = " + SightBookText.Number(run, "F3") + " морской мили",
                "Сдвинь всю линию параллельно по курсу на ход лодки; её направление не вращай.",
                "P(перенесённая точка линии) = " + SightBookText.Coordinate(transferred.TangentLatDeg, true) +
                ", " + SightBookText.Coordinate(transferred.TangentLonDeg, false),
                "Исходная точка P = " + SightBookText.Coordinate(original.TangentLatDeg, true) + ", " + SightBookText.Coordinate(original.TangentLonDeg, false) +
                ". Это первый проход; решатель затем уточняет касательные у найденного места.", next);
        }

        internal static string ComposeSolution(bool usedTriangle, CockedHatResult hat)
        {
            var sb = new StringBuilder();
            sb.Append("\n[F4] КАК ПОЛУЧЕНО ОПРЕДЕЛЁННОЕ МЕСТО?\n");
            if (!usedTriangle)
            {
                sb.Append("Пересечение линий №1/№2 с повторным расчётом высот и азимутов около первого приближения (два прохода).\n");
                return sb.ToString();
            }
            sb.Append(hat.UsedBestPairFallback
                ? "Опубликован уточнённый FIX по паре, не центроид. Идентификаторы выбранной пары и момент FIX пары не возвращаются решателем; подробные переносы для опубликованного FIX недоступны. Треугольник ниже, если есть, — диагностика трёх линий, не след решения пары.\n"
                : "Опубликован центроид трёх попарных пересечений после уточнения (два прохода).\n");
            // A degenerate fallback returns no triangle metrics, not a zero-size hat.
            if (hat.SideANm == 0 && hat.SideBNm == 0 && hat.SideCNm == 0)
            {
                sb.Append("Треугольник вырожден или не построен; числовых метрик нет.\n");
                return sb.ToString();
            }
            AppendTriangle(sb, hat);
            return sb.ToString();
        }

        private static void AppendTriangle(StringBuilder sb, CockedHatResult hat)
        {
            AppendVertex(sb, "A(пересечение средней и последней линий)", hat.VertexALatDeg, hat.VertexALonDeg);
            AppendVertex(sb, "B(пересечение последней и первой линий)", hat.VertexBLatDeg, hat.VertexBLonDeg);
            AppendVertex(sb, "C(пересечение первой и средней линий)", hat.VertexCLatDeg, hat.VertexCLonDeg);
            SightBookText.Card(sb, "F5", "КАКОВЫ СТОРОНЫ ТРЕУГОЛЬНИКА?",
                "A, B, C(вершины) записаны выше, после уточнения решателя.",
                "Измерь расстояния AB, BC, CA. Мод использует расстояния по большой окружности.",
                "AB = " + SightBookText.Number(hat.SideANm, "F6") + "; BC = " + SightBookText.Number(hat.SideBNm, "F6") +
                "; CA = " + SightBookText.Number(hat.SideCNm, "F6") + " морской мили",
                "Сумма любых двух сторон не меньше третьей. Шесть знаков оставлены для независимой проверки площади.", "F6");
            SightBookText.Card(sb, "F6", "КАКАЯ ПЛОЩАДЬ ТРЕУГОЛЬНИКА?",
                "AB = " + SightBookText.Number(hat.SideANm, "F6") + "; BC = " + SightBookText.Number(hat.SideBNm, "F6") +
                "; CA = " + SightBookText.Number(hat.SideCNm, "F6") + " морской мили",
                "Полупериметр p = (BC + CA + AB) / 2. Площадь по Герону: √(p × (p − BC) × (p − CA) × (p − AB)).",
                "Площадь = " + SightBookText.Number(hat.AreaNm2, "F6") + " квадратной морской мили",
                "Не сравнивай точную площадь с площадью, посчитанной по грубо округлённым сторонам.", "F7");
            SightBookText.Card(sb, "F7", "МОЖНО ЛИ ПУБЛИКОВАТЬ ЦЕНТР?",
                "R(максимальное расстояние от центроида до вершины) = " + SightBookText.Number(hat.RadiusNm, "F6") + " морской мили",
                "Сравни радиус с порогом публикации центра " + SightBookText.Number(RunningFixCalculator.MaxPublishRadiusNm) + " морских миль.",
                hat.UsedBestPairFallback ? "Центр не опубликован; использована пара." : "Использован центроид треугольника.",
                "R не является радиусом описанной окружности; опубликованная точка пары может не совпадать с центроидом.", "замороженный результат");
        }

        private static void AppendVertex(StringBuilder sb, string name, double lat, double lon)
        {
            sb.Append(name).Append(" = ").Append(SightBookText.Coordinate(lat, true))
                .Append(", ").Append(SightBookText.Coordinate(lon, false)).Append('\n');
        }
    }
}
