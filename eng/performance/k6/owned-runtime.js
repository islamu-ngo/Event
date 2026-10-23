import http from 'k6/http';
import { check, sleep } from 'k6';
import { sha256 } from 'k6/crypto';
import { Counter, Rate } from 'k6/metrics';

// The C# owner supplies an already admitted AppHost origin and disposable output path.
const origin = __ENV.LOAD_ORIGIN;
const target = __ENV.LOAD_TARGET;
const mode = __ENV.LOAD_MODE;
const digest = __ENV.LOAD_DIGEST;
if (!/^http:\/\/127\.0\.0\.1:[1-9][0-9]*$/.test(origin) ||
    target !== `${origin}/api/language` ||
    !/^[a-f0-9]{64}$/.test(digest) ||
    (mode !== 'closed' && mode !== 'open') || !__ENV.LOAD_SUMMARY) {
    throw new Error('load_configuration_not_admitted');
}

const attempts = new Counter('load_attempts');
const responses = new Counter('load_responses');
const correct = new Counter('load_correct');
const correctness = new Rate('load_correctness');
const thresholds = {
    load_correctness: ['rate==1'],
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
    load_responses: ['count>0'],
};
if (mode === 'open') thresholds.dropped_iterations = ['count==0'];

export const options = {
    scenarios: {
        lookup: mode === 'closed'
            ? { executor: 'constant-vus', vus: 4, duration: '5s', gracefulStop: '5s' }
            : { executor: 'constant-arrival-rate', rate: 8, timeUnit: '1s', duration: '5s',
                preAllocatedVUs: 4, maxVUs: 4, gracefulStop: '5s' },
    },
    thresholds,
    maxRedirects: 0,
    summaryTrendStats: ['p(95)'],
    systemTags: ['name', 'method'],
};

export default function () {
    attempts.add(1);
    const response = http.get(target, {
        timeout: '2s', redirects: 0, tags: { name: 'public-language-lookup' },
        headers: { Accept: 'application/json' },
    });
    responses.add(response.status > 0 ? 1 : 0);
    const valid = response.status === 200 &&
        /^application\/json(?:;|$)/i.test(response.headers['Content-Type'] || '') &&
        typeof response.body === 'string' && sha256(response.body, 'hex') === digest;
    correct.add(valid ? 1 : 0);
    correctness.add(valid);
    check(valid, { 'public lookup remains unchanged': value => value });
    sleep(0.1);
}

export function handleSummary(data) {
    function value(name, statistic) {
        const metric = data.metrics[name];
        if (!metric || !Number.isFinite(metric.values[statistic])) {
            throw new Error('load_required_metric_missing');
        }
        return metric.values[statistic];
    }
    const summary = {
        schema: 1, mode,
        attempts: value('load_attempts', 'count'),
        responses: value('load_responses', 'count'),
        correct: value('load_correct', 'count'),
        iterations: value('iterations', 'count'),
        httpRequests: value('http_reqs', 'count'),
        correctnessRate: value('load_correctness', 'rate'),
        httpFailureRate: value('http_req_failed', 'rate'),
        dropped: mode === 'open' ? value('dropped_iterations', 'count') : 0,
        p95Milliseconds: value('http_req_duration', 'p(95)'),
    };
    return { [__ENV.LOAD_SUMMARY]: JSON.stringify(summary) };
}
