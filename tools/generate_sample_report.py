#!/usr/bin/env python3
"""Генератор примерного отчёта LaptopInspector на синтетических данных.

Скрипт не обращается к системе и не собирает реальные идентификаторы. Он нужен для
документации, визуальной проверки форматов и CI-smoke validation JSON Schema.
"""

from __future__ import annotations

import hashlib
import html
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "sample_reports"
BASE = "LaptopInspector_SampleLaptop_20261125_120000"


def build_report() -> dict:
    collectors = [
        collector("runtime.environment", "success", 42, {"computerName": "SAMPLE-LAPTOP", "osDescription": "Microsoft Windows 11 Pro 24H2", "processorCount": 16}),
        collector("native.storage", "success", 121, {"physicalDrives": [{"index": 0, "deviceDescriptor": {"productId": "Sample NVMe 1TB", "busType": "NVMe"}, "nvmeHealth": {"criticalWarning": 0, "percentageUsed": 12, "temperatureCelsius": 42, "mediaAndDataIntegrityErrors": {"raw": "0"}, "dataUnitsWritten": {"raw": "12345678", "estimatedTerabytesDecimal": 6.321}}}]}),
        collector("native.battery", "success", 85, {"batteries": [{"manufacturer": "Sample Battery Co", "deviceName": "BAT0", "information": {"designedCapacityMWh": 57000, "fullChargedCapacityMWh": 45200, "wearPercent": 20.7, "cycleCount": 412}, "statusInfo": {"capacityMWh": 32100, "voltageMillivolts": 11850, "charging": False, "discharging": True, "acOnline": False}}]}),
        collector("native.display", "success", 64, {"dxgiAdapters": [{"description": "Sample Integrated GPU", "dedicatedVideoMemoryBytes": 134217728, "sharedSystemMemoryBytes": 8589934592, "outputs": [{"deviceName": "\\\\.\\DISPLAY1", "attachedToDesktop": True}]}], "displayConfig": {"pathCount": 1}}),
        collector("security.overview", "success", 210, {"defender": {"RealTimeProtectionEnabled": True, "DefenderSignaturesOutOfDate": False}, "bitLockerVolumes": [{"MountPoint": "C:", "ProtectionStatus": "On"}], "uac": {"EnableLUA": 1}, "rdp": {"terminalServer": {"fDenyTSConnections": 1}}, "smb1": {"State": "Disabled"}}),
        collector("stability.events", "success", 333, {"bugChecks": [], "wheaLogger": [], "kernelPower41": [], "diskAndFileSystemErrors": [], "displayTdr4101": [], "applicationCrashGroups": []}),
        collector("benchmark.synthetic", "success", 3100, {"cpuSingleThread": {"score": 48.2}, "cpuMultiThread": {"score": 421.5}, "memory": {"copyGibPerSecond": 18.4, "pointerChaseNanosecondsPerStep": 6.2}, "disk": {"status": "skipped", "reason": "not confirmed"}}),
        collector("scan.coverage", "success", 2, {"sections": [{"section": "4.1", "title": "Идентификация системы"}, {"section": "4.17", "title": "Журналы событий и стабильность"}]})
    ]
    findings = [
        finding("BAT-001", "Medium", "Battery", "Износ аккумулятора выше 20%", "Оценочный износ аккумулятора: 20.7%.", "Проверьте автономность; при заметной деградации планируйте замену батареи.", "$.native.battery.batteries[0].information.wearPercent"),
        finding("COV-001", "Info", "Coverage", "Отчёт содержит карту покрытия", "scan.coverage перечисляет покрытие разделов 4.1–4.19.", "Используйте карту покрытия, чтобы понять доступность источников.", "$.scan.coverage.sections")
    ]
    data = {item["id"]: item["data"] for item in collectors}
    data["health.rules"] = {
        "ruleLoadErrors": [],
        "penaltiesByBucket": {"hardware": 7, "security": 0, "stability": 0},
        "formula": "overall = hardware*0.45 + security*0.35 + stability*0.20; bucket score = 100 - capped penalties"
    }
    report = {
        "schemaVersion": "1.0.0",
        "metadata": {
            "generator": "LaptopInspector",
            "generatorVersion": "0.8.0-stage8-sample",
            "createdAt": "2026-11-25T12:00:00+00:00",
            "durationMs": 3957,
            "isAdministrator": True,
            "language": "ru",
            "computerName": "SAMPLE-LAPTOP",
            "operatingSystem": "Microsoft Windows 11 Pro 24H2",
            "requestedFormats": ["html", "json", "txt", "md", "csv"],
            "anonymized": True,
            "reportHashSha256": None
        },
        "summary": {
            "hardwareHealthScore": 93,
            "securityScore": 100,
            "stabilityScore": 100,
            "overallScore": 97,
            "verdict": "Отличное",
            "penaltiesByCategory": {"hardware": 7, "security": 0, "stability": 0}
        },
        "collectors": collectors,
        "findings": findings,
        "data": data
    }
    unsigned = json.dumps(report, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    report["metadata"]["reportHashSha256"] = hashlib.sha256(unsigned).hexdigest()
    return report


def collector(identifier: str, status: str, duration_ms: int, data: dict) -> dict:
    return {
        "id": identifier,
        "status": status,
        "duration": f"00:00:00.{duration_ms:07d}",
        "data": data,
        "findings": [],
        "errors": [],
        "skipReason": None
    }


def finding(rule_id: str, severity: str, category: str, title: str, details: str, recommendation: str, evidence: str) -> dict:
    return {
        "ruleId": rule_id,
        "severity": severity,
        "category": category,
        "title": title,
        "details": details,
        "recommendation": recommendation,
        "evidence": evidence
    }


def write_json(report: dict) -> None:
    (OUT / f"{BASE}.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_txt(report: dict) -> None:
    lines = [
        "LaptopInspector — пример отчёта на синтетических данных",
        "=" * 80,
        f"Дата: {report['metadata']['createdAt']}",
        f"Версия: {report['metadata']['generatorVersion']}",
        f"Компьютер: {report['metadata']['computerName']}",
        f"Overall: {report['summary']['overallScore']}/100 ({report['summary']['verdict']})",
        "",
        "Проблемы и рекомендации",
        "-" * 80,
    ]
    for item in report["findings"]:
        lines += [f"[{item['severity']}] {item['ruleId']} {item['title']}", f"  {item['details']}", f"  {item['recommendation']}", ""]
    (OUT / f"{BASE}.txt").write_text("\n".join(lines), encoding="utf-8")


def write_md(report: dict) -> None:
    lines = [
        "# LaptopInspector — пример отчёта",
        "",
        "Синтетические данные, не соответствуют реальному устройству.",
        "",
        "| Метрика | Значение |",
        "|---|---:|",
        f"| Hardware | {report['summary']['hardwareHealthScore']}/100 |",
        f"| Security | {report['summary']['securityScore']}/100 |",
        f"| Stability | {report['summary']['stabilityScore']}/100 |",
        f"| Overall | {report['summary']['overallScore']}/100 |",
        "",
        "## Findings",
        ""
    ]
    for item in report["findings"]:
        lines += [f"### {item['ruleId']} — {item['title']}", "", f"- Severity: `{item['severity']}`", f"- Details: {item['details']}", f"- Recommendation: {item['recommendation']}", ""]
    (OUT / f"{BASE}.md").write_text("\n".join(lines), encoding="utf-8")


def write_html(report: dict) -> None:
    findings = "".join(f"<li><b>{html.escape(item['severity'])}</b> {html.escape(item['ruleId'])}: {html.escape(item['title'])}<br><span>{html.escape(item['details'])}</span></li>" for item in report["findings"])
    page = f"""<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>LaptopInspector sample report</title>
<style>body{{font:15px/1.55 system-ui,sans-serif;margin:32px;max-width:980px}}.cards{{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}}.card{{border:1px solid #dbe3ef;border-radius:14px;padding:16px;text-align:center}}.score{{font-size:34px;font-weight:800;color:#2563eb}}li{{margin:12px 0}}</style></head>
<body><h1>LaptopInspector — пример отчёта</h1><p>Синтетические данные, не соответствуют реальному устройству.</p>
<div class="cards">
<div class="card"><div>Hardware</div><div class="score">{report['summary']['hardwareHealthScore']}</div></div>
<div class="card"><div>Security</div><div class="score">{report['summary']['securityScore']}</div></div>
<div class="card"><div>Stability</div><div class="score">{report['summary']['stabilityScore']}</div></div>
<div class="card"><div>Overall</div><div class="score">{report['summary']['overallScore']}</div></div>
</div><h2>Findings</h2><ul>{findings}</ul><h2>Collectors</h2><p>{len(report['collectors'])} collectors in sample.</p></body></html>"""
    (OUT / f"{BASE}.html").write_text(page, encoding="utf-8")


def write_csv_zip(report: dict) -> None:
    with ZipFile(OUT / f"{BASE}_csv.zip", "w", ZIP_DEFLATED) as archive:
        archive.writestr("scores.csv", "metric,value\noverallScore,%s\nverdict,%s\n" % (report["summary"]["overallScore"], report["summary"]["verdict"]))
        rows = ["ruleId,severity,category,title"]
        rows += [f"{item['ruleId']},{item['severity']},{item['category']},{item['title']}" for item in report["findings"]]
        archive.writestr("findings.csv", "\n".join(rows) + "\n")
        archive.writestr("collectors.csv", "id,status\n" + "\n".join(f"{item['id']},{item['status']}" for item in report["collectors"]) + "\n")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    report = build_report()
    write_json(report)
    write_txt(report)
    write_md(report)
    write_html(report)
    write_csv_zip(report)
    print(f"Sample reports written to {OUT}")


if __name__ == "__main__":
    main()
