lexer grammar moirai_lexer;

STRING : ('"' (~[\\"])* '"') | ('\''(~[\\'])*  '\'');
NULL: 'null';
SPACE: [ \t]+ -> skip;
LINE_BREAK: ('\r\n' | '\r' | '\n');
COMMENT
  :  '#' ~( '\r' | '\n' )* LINE_BREAK -> skip
  ;

SCOPE_OPEN: '{';
SCOPE_CLOSE: '}';
RULE: 'rule';
PROP: 'prop';
ENUM: 'enum';
SET: 'set';
COMMA: ',';

TRUE: 'true';
FALSE: 'false';
DOT: '.' ;
NEQ: '!=' ;
EQ: '=';

VAR_ID: '$' (ALPHA|DIGIT)(ALPHA|DIGIT|'_')*;
ACTION_ID: '@' [a-z][a-z_]*;
PROP_ID: '%' [a-z][a-z_]*;

ID : ALPHA (ALPHA|'_')* ;
NUMBER: DIGIT+ ;
fragment
DIGIT   :   ('0'..'9');
fragment
ALPHA   :   ('a'..'z'|'A'..'Z');