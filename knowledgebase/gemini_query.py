import os
import json
import argparse
import http.client
import sys

def load_config():
    config_path = os.path.join(os.path.dirname(__file__), 'config.json')
    if not os.path.exists(config_path):
        print(f"Error: {config_path} not found. Please create it from config.json.example.")
        sys.exit(1)
    with open(config_path, 'r', encoding='utf-8') as f:
        return json.load(f)

def call_gemini(api_key, model, prompt):
    # Using v1beta for better features
    host = "generativelanguage.googleapis.com"
    endpoint = f"/v1beta/models/{model}:generateContent?key={api_key}"
    
    payload = {
        "contents": [{
            "parts": [{"text": prompt}]
        }]
    }
    
    headers = {"Content-Type": "application/json"}
    
    conn = http.client.HTTPSConnection(host)
    conn.request("POST", endpoint, body=json.dumps(payload), headers=headers)
    
    response = conn.getresponse()
    data = response.read()
    conn.close()
    
    if response.status != 200:
        print(f"API Error: HTTP {response.status}")
        print(data.decode('utf-8'))
        sys.exit(1)
        
    result = json.loads(data.decode('utf-8'))
    try:
        return result['candidates'][0]['content']['parts'][0]['text']
    except (KeyError, IndexError):
        print("Unexpected response format.")
        print(json.dumps(result, indent=2))
        sys.exit(1)

def main():
    parser = argparse.ArgumentParser(description="Query Gemini API")
    parser.add_argument("prompt", help="The question or prompt to ask Gemini")
    parser.add_argument("-o", "--output", help="Save output to a text file")
    
    args = parser.parse_args()
    
    config = load_config()
    api_key = config.get("api_key")
    model = config.get("model", "gemini-1.5-flash")
    
    if not api_key:
        print("Error: API key is missing in config.json.")
        sys.exit(1)
        
    print(f"Querying Gemini ({model})...")
    answer = call_gemini(api_key, model, args.prompt)
    
    if args.output:
        with open(args.output, 'w', encoding='utf-8') as f:
            f.write(answer)
        print(f"Response saved to: {args.output}")
    else:
        print("
--- Response ---
")
        print(answer)

if __name__ == "__main__":
    main()
