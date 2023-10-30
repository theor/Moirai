grammar storygen;
r: (COMMENT | LINE_BREAK)* action+ ;
action: ACTION_ID LINE_BREAK effect (effect)* ;
effect: (set | assign | call) SPACE* LINE_BREAK+ SPACE*;
set: 'set'  path '=' value;
value: string | path | bool | number | NULL;
string: STRING ;
STRING : '"' (~[\\"])* '"';
bool: 'true' | 'false';
assign: VAR_ID '=' call;
call : ID  ((expr (',' expr)* ));
expr : value (op value)? ;
op : '=' | '!=' ;
path : VAR_ID ('.' ID)* | ID;
number: NUMBER;
NULL: 'null';
VAR_ID: '$' (ALPHA|DIGIT)(ALPHA|DIGIT|'_')*;
ACTION_ID: '@' [a-z][a-z_]*;
ID : ALPHA (ALPHA|'_')* ;
SPACE: [ \t]+ -> skip;
LINE_BREAK: ('\r\n' | '\r' | '\n');
COMMENT
  :  '#' ~( '\r' | '\n' )* LINE_BREAK -> skip
  ;
NUMBER: DIGIT+ ;
fragment
DIGIT   :   ('0'..'9');
fragment
ALPHA   :   ('a'..'z'|'A'..'Z');